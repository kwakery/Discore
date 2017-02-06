﻿using Nito.AsyncEx;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Discore.WebSocket.Net
{
    partial class Gateway : IDiscordGateway, IDisposable
    {
        public Shard Shard { get { return shard; } }
        public DiscoreWebSocketState SocketState { get { return socket.State; } }

        public event EventHandler OnReconnected;
        public event EventHandler<GatewayDisconnectCode> OnFatalDisconnection;

        /// <summary>
        /// Maximum number of missed heartbeats before timing out.
        /// </summary>
        const int HEARTBEAT_TIMEOUT_MISSED_PACKETS = 3;

        const int GATEWAY_VERSION = 5;

        DiscordWebSocketApplication app;
        Shard shard;

        CancellationTokenSource taskCancelTokenSource;

        DiscoreWebSocket socket;
        DiscoreLogger log;

        int sequence;
        string sessionId;
        int heartbeatInterval;
        int heartbeatTimeoutAt;
        Task heartbeatTask;

        bool isDisposed;

        bool wasRateLimited;

        /// <summary>
        /// Will be true while ConnectAsync is running.
        /// </summary>
        bool isConnecting;

        bool isReconnecting;
        Task reconnectTask;
        CancellationTokenSource reconnectCancelTokenSource;

        DiscoreCache cache;

        GatewayRateLimiter connectionRateLimiter;
        GatewayRateLimiter outboundEventRateLimiter;
        GatewayRateLimiter gameStatusUpdateRateLimiter;

        AsyncManualResetEvent helloPayloadEvent;

        internal Gateway(DiscordWebSocketApplication app, Shard shard)
        {
            this.app = app;
            this.shard = shard;

            cache = shard.Cache;

            string logName = $"Gateway#{shard.Id}";
               
            log = new DiscoreLogger(logName);

            helloPayloadEvent = new AsyncManualResetEvent();

            // Up-to-date rate limit parameters: https://discordapp.com/developers/docs/topics/gateway#rate-limiting
            connectionRateLimiter = new GatewayRateLimiter(5, 1); // One connection attempt per 5 seconds
            outboundEventRateLimiter = new GatewayRateLimiter(60, 120); // 120 outbound events every 60 seconds
            gameStatusUpdateRateLimiter = new GatewayRateLimiter(60, 5); // 5 status updates per minute

            InitializePayloadHandlers();
            InitializeDispatchHandlers();
            
            socket = new DiscoreWebSocket(WebSocketDataType.Json, logName);
            socket.OnError += Socket_OnError;
            socket.OnMessageReceived += Socket_OnMessageReceived;
        }

        /// <param name="forceFindNew">Whether to call the HTTP forcefully, or use the local cached value.</param>
        async Task<string> GetGatewayUrlAsync(CancellationToken cancellationToken, bool forceFindNew = false)
        {
            DiscoreLocalStorage localStorage = await DiscoreLocalStorage.GetInstanceAsync().ConfigureAwait(false);

            string gatewayUrl = localStorage.GatewayUrl;
            if (forceFindNew || string.IsNullOrWhiteSpace(gatewayUrl))
            {
                gatewayUrl = await app.HttpApi.Gateway.Get(cancellationToken).ConfigureAwait(false);

                localStorage.GatewayUrl = gatewayUrl;
                await localStorage.SaveAsync().ConfigureAwait(false);
            }

            return gatewayUrl;
        }

        /// <param name="gatewayResume">Will send a resume payload instead of an identify upon reconnecting when true.</param>
        /// <exception cref="ObjectDisposedException">Thrown if this gateway connection has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if already connected or currently connecting.</exception>
        /// <exception cref="TaskCanceledException"></exception>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken, bool gatewayResume = false)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(socket), "Cannot use a disposed gateway connection.");
            if (socket.State != DiscoreWebSocketState.Closed)
                throw new InvalidOperationException("Failed to connect, the Gateway is already connected or connecting.");

            isConnecting = true;

            try
            {
                log.LogVerbose($"[ConnectAsync] Attempting to connect - gatewayResume: {gatewayResume}");

                // Reset gateway state only if not resuming
                if (!gatewayResume)
                    Reset();

                // Get the gateway url
                string gatewayUrl = await GetGatewayUrlAsync(cancellationToken).ConfigureAwait(false);

                log.LogVerbose($"[ConnectAsync] gatewayUrl: {gatewayUrl}");

                // If was rate limited from last disconnect, wait extra time.
                if (wasRateLimited)
                {
                    wasRateLimited = false;
                    await Task.Delay(connectionRateLimiter.ResetTimeSeconds * 1000).ConfigureAwait(false);
                }

                // Check with the connection rate limiter.
                await connectionRateLimiter.Invoke().ConfigureAwait(false);

                // Reset the hello event so we know when the connection was successful.
                helloPayloadEvent.Reset();

                // Attempt to connect to the WebSocket API.
                bool connectedToSocket;

                try
                {
                    connectedToSocket = await socket.ConnectAsync($"{gatewayUrl}/?encoding=json&v={GATEWAY_VERSION}", cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    connectedToSocket = false;
                }

                if (connectedToSocket)
                {
                    log.LogVerbose("[ConnectAsync] Awaiting hello...");

                    // Give Discord 10s to send Hello payload
                    const int helloTimeout = 10 * 1000;
                    Task helloWaitTask = await Task.WhenAny(helloPayloadEvent.WaitAsync(cancellationToken), Task.Delay(helloTimeout, cancellationToken))
                        .ConfigureAwait(false);

                    if (helloWaitTask.IsCanceled)
                        throw new TaskCanceledException(helloWaitTask);

                    // Check if the payload was recieved or if we timed out.
                    if (heartbeatInterval > 0)
                    {
                        taskCancelTokenSource = new CancellationTokenSource();

                        // Handshake was successful, begin the heartbeat loop
                        heartbeatTask = HeartbeatLoop();

                        // Send resume or identify payload
                        if (gatewayResume)
                            await SendResumePayload().ConfigureAwait(false);
                        else
                            await SendIdentifyPayload().ConfigureAwait(false);

                        log.LogVerbose("[ConnectAsync] Connection successful.");
                        return true;
                    }
                    else if (socket.State == DiscoreWebSocketState.Open)
                    {
                        log.LogError("[ConnectAsync] Timed out waiting for hello.");

                        // We timed out, but the socket is still connected.
                        await socket.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Since we failed to connect, try and find the new gateway url.
                    string newGatewayUrl = await GetGatewayUrlAsync(cancellationToken, true).ConfigureAwait(false);
                    if (gatewayUrl != newGatewayUrl)
                    {
                        // If the endpoint did change, overwrite it in storage.
                        DiscoreLocalStorage localStorage = await DiscoreLocalStorage.GetInstanceAsync().ConfigureAwait(false);
                        localStorage.GatewayUrl = newGatewayUrl;

                        await localStorage.SaveAsync().ConfigureAwait(false);
                    }
                }

                log.LogError("[ConnectAsync] Failed to connect.");
                return false;
            }
            finally
            {
                isConnecting = false;
            }
        }

        /// <exception cref="ObjectDisposedException">Thrown if this gateway connection has been disposed.</exception>
        /// <exception cref="TaskCanceledException"></exception>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(socket), "Cannot use a disposed gateway connection.");

            log.LogVerbose("[DisconnectAsync] Disconnecting...");

            // Cancel reconnection
            await CancelReconnect().ConfigureAwait(false);

            // Disconnect the socket
            if (socket.State == DiscoreWebSocketState.Open)
                await socket.DisconnectAsync(cancellationToken).ConfigureAwait(false);

            log.LogVerbose("[DisconnectAsync] Socket disconnected...");

            taskCancelTokenSource.Cancel();

            // Wait for heartbeat loop to finish
            if (heartbeatTask != null)
                await heartbeatTask.ConfigureAwait(false);

            log.LogVerbose("[DisconnectAsync] Disconnection successful.");
        }

        void Reset()
        {
            sequence = 0;
            heartbeatInterval = 0;
            sessionId = null;

            shard.User = null;
        }

        async Task HeartbeatLoop()
        {
            bool timedOut = false;

            // Set timeout
            heartbeatTimeoutAt = Environment.TickCount + (heartbeatInterval * HEARTBEAT_TIMEOUT_MISSED_PACKETS);

            // Run heartbeat loop until socket is ended or timed out
            while (socket.State == DiscoreWebSocketState.Open)
            {
                if (TimeHelper.HasTickCountHit(heartbeatTimeoutAt))
                {
                    timedOut = true;
                    break;
                }

                try
                {
                    await SendHeartbeatPayload().ConfigureAwait(false);

                    await Task.Delay(heartbeatInterval, taskCancelTokenSource.Token).ConfigureAwait(false);
                }
                // Two valid exceptions are a cancellation and a dispose before full-disconnect.
                catch (TaskCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    // Should never happen and is not considered fatal, but log it just in case.
                    log.LogError($"[HeartbeatLoop] {ex}");
                }
            }

            // If we have timed out and the socket was not disconnected, attempt to reconnect.
            if (timedOut && socket.State == DiscoreWebSocketState.Open && !isReconnecting && !isDisposed)
            {
                log.LogInfo("[HeartbeatLoop] Connection timed out, reconnecting...");

                // Start reconnecting, since the socket is still open we cannot resume...
                BeginReconnect();

                // Let this task end, as it will be overwritten once reconnection completes.
            }
        }

        /// <param name="gatewayResume">Whether to perform a full-reconnect or just a resume.</param>
        void BeginReconnect(bool gatewayResume = false)
        {
            // Since a reconnect can be started from multiple threads,
            // ensure that we do not enter this loop simultaneously.
            // We also do not want to attempt a reconnection, if an
            // error occured before we were finished initiating a
            // connection.
            if (!isReconnecting && !isConnecting)
            {
                reconnectCancelTokenSource = new CancellationTokenSource();

                isReconnecting = true;

                reconnectTask = ReconnectLoop(gatewayResume);
            }
        }

        async Task CancelReconnect()
        {
            if (isReconnecting)
            {
                reconnectCancelTokenSource.Cancel();
                await reconnectTask.ConfigureAwait(false);
            }
        }

        async Task ReconnectLoop(bool gatewayResume)
        {
            log.LogVerbose($"[ReconnectLoop] Begin - gatewayResume: {gatewayResume}");

            // Disable socket error handling until we have reconnected.
            // This avoids the socket performing its own disconnection
            // procedure from an error, which may occur while we reconnect,
            // especially if this reconnection originated from a timeout.
            socket.IgnoreSocketErrors = true;

            // Make sure we disconnect first
            if (socket.State == DiscoreWebSocketState.Open)
                await socket.DisconnectAsync(reconnectCancelTokenSource.Token).ConfigureAwait(false);

            log.LogVerbose("[ReconnectLoop] Socket disconnected...");

            // Let heartbeat task finish
            if (heartbeatTask != null && heartbeatTask.Status == TaskStatus.Running)
                await heartbeatTask.ConfigureAwait(false);

            log.LogVerbose("[ReconnectLoop] Heartbeat loop completed, attempting to reconnect...");

            // Keep trying to connect until canceled
            while (!isDisposed && !reconnectCancelTokenSource.IsCancellationRequested)
            {
                try
                {
                    if (await ConnectAsync(reconnectCancelTokenSource.Token, gatewayResume).ConfigureAwait(false))
                        break;
                }
                catch (Exception ex)
                {
                    log.LogError($"[ReconnectLoop] {ex}");
                }
            }

            // Restore socket errors regardless of cancellation or success.
            socket.IgnoreSocketErrors = false;

            if (!reconnectCancelTokenSource.IsCancellationRequested)
            {
                log.LogInfo("[ReconnectLoop] Reconnect successful.");
                isReconnecting = false;

                if (!isDisposed)
                    OnReconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Socket_OnError(object sender, Exception e)
        {
            DiscoreWebSocketException dex = e as DiscoreWebSocketException;
            if (dex != null)
            {
                GatewayDisconnectCode code = (GatewayDisconnectCode)dex.ErrorCode;
                switch (code)
                {
                    case GatewayDisconnectCode.InvalidShard:
                    case GatewayDisconnectCode.AuthenticationFailed:
                    case GatewayDisconnectCode.ShardingRequired:
                        // Not safe to reconnect
                        log.LogError($"[{code} ({(int)code})] Unsafe to continue, NOT reconnecting gateway.");
                        OnFatalDisconnection?.Invoke(this, code);
                        break;
                    case GatewayDisconnectCode.InvalidSeq:
                    case GatewayDisconnectCode.SessionTimeout:
                    case GatewayDisconnectCode.UnknownError:
                        // Safe to reconnect, but needs a full reconnect.
                        BeginReconnect();
                        break;
                    case GatewayDisconnectCode.NotAuthenticated:
                        // This really should never happen, but will require a full-reconnect.
                        log.LogWarning("Sent gateway payload before we identified!");
                        BeginReconnect();
                        break;
                    case GatewayDisconnectCode.RateLimited:
                        // Doesn't require a full-reconnection, but we need to wait a bit.
                        log.LogWarning("Gateway is being rate limited!");
                        wasRateLimited = true;
                        BeginReconnect(true);
                        break;
                    default:
                        // Safe to just resume
                        BeginReconnect(true);
                        break;
                }
            }
            else
            {
                // If it is a socket error, we can resume since the
                // socket was closed abnormally. Otherwise we will
                // need a full re-connect, however this should never happen.
                BeginReconnect(e is WebSocketException);
            }
        }

        private void Socket_OnMessageReceived(object sender, DiscordApiData e)
        {
            GatewayOPCode op = (GatewayOPCode)e.GetInteger("op");
            DiscordApiData data = e.Get("d");

            PayloadCallback callback;
            if (payloadHandlers.TryGetValue(op, out callback))
                callback(e, data);
            else
                log.LogWarning($"Missing handler for payload: {op}({(int)op})");
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;

                socket?.Dispose();
                taskCancelTokenSource?.Dispose();
                reconnectCancelTokenSource?.Dispose();
            }
        }
    }
}
