﻿using System;
using System.Collections.Generic;
using Discore.Http.Net;

namespace Discore.WebSocket
{
    /// <summary>
    /// Direct message channels represent a one-to-one conversation between two users, outside of the scope of guilds.
    /// </summary>
    public sealed class DiscordDMChannel : DiscordChannel, ITextChannel
    {
        /// <summary>
        /// The id of the last message sent in this DM.
        /// </summary>
        public Snowflake LastMessageId { get; private set; }

        /// <summary>
        /// Gets the user on the other end of this channel.
        /// </summary>
        public DiscordUser Recipient { get; private set; }

        Shard shard;
        HttpChannelsEndpoint channelsHttp;

        internal DiscordDMChannel(Shard shard) 
            : base(shard, DiscordChannelType.DirectMessage)
        {
            this.shard = shard;

            channelsHttp = shard.Application.InternalHttpApi.Channels;
        }

        /// <summary>
        /// Sends a message to this channel.
        /// </summary>
        /// <param name="content">The message text content.</param>
        /// <param name="splitIfTooLong">Whether this message should be split into multiple messages if too long.</param>
        /// <param name="tts">Whether this should be played over text-to-speech.</param>
        /// <returns>Returns the created message (or first if split into multiple).</returns>
        public DiscordMessage SendMessage(string content, bool splitIfTooLong = false, bool tts = false)
        {
            DiscordApiData firstOrOnlyMessageData = null;

            if (splitIfTooLong && content.Length > DiscordMessage.MAX_CHARACTERS)
            {
                SplitSendMessage(content,
                    message =>
                    {
                        DiscordApiData msgData = channelsHttp.CreateMessage(Id, message, tts);

                        if (firstOrOnlyMessageData == null)
                            firstOrOnlyMessageData = msgData;
                    });
            }
            else
                firstOrOnlyMessageData = channelsHttp.CreateMessage(Id, content, tts);

            DiscordMessage msg = new DiscordMessage(shard);
            msg.Update(firstOrOnlyMessageData);

            return msg;
        }

        /// <summary>
        /// Sends a message with a file attachment to this channel.
        /// </summary>
        /// <param name="fileAttachment">The file data to attach.</param>
        /// <param name="content">The message text content.</param>
        /// <param name="splitIfTooLong">Whether this message should be split into multiple messages if too long.</param>
        /// <param name="tts">Whether this should be played over text-to-speech.</param>
        /// <returns>Returns the created message (or first if split into multiple).</returns>
        public DiscordMessage SendMessage(byte[] fileAttachment, string content = null, bool splitIfTooLong = false, bool tts = false)
        {
            DiscordApiData firstOrOnlyMessageData = null;

            if (splitIfTooLong && content.Length > DiscordMessage.MAX_CHARACTERS)
            {
                SplitSendMessage(content,
                    message =>
                    {
                        if (firstOrOnlyMessageData == null)
                        {
                            DiscordApiData msgData = channelsHttp.UploadFile(Id, fileAttachment, message, tts);
                            firstOrOnlyMessageData = msgData;
                        }
                        else
                            channelsHttp.CreateMessage(Id, message, tts);
                    });
            }
            else
                firstOrOnlyMessageData = channelsHttp.UploadFile(Id, fileAttachment, content, tts);

            DiscordMessage msg = new DiscordMessage(shard);
            msg.Update(firstOrOnlyMessageData);

            return msg;
        }

        void SplitSendMessage(string content, Action<string> createMessageCallback)
        {
            int i = 0;
            while (i < content.Length)
            {
                int maxChars = Math.Min(DiscordMessage.MAX_CHARACTERS, content.Length - i);
                int lastNewLine = content.LastIndexOf('\n', i + maxChars - 1, maxChars - 1);

                string subMessage;
                if (lastNewLine > -1)
                    subMessage = content.Substring(i, lastNewLine - i);
                else
                    subMessage = content.Substring(i, maxChars);

                if (!string.IsNullOrWhiteSpace(subMessage))
                    createMessageCallback(subMessage);

                i += subMessage.Length;
            }
        }

        /// <summary>
        /// Deletes a list of messages in one API call.
        /// Much quicker than calling Delete() on each message instance.
        /// </summary>
        /// <returns>Returns whether the operation was successful.</returns>
        public bool BulkDeleteMessages(IEnumerable<Snowflake> messageIds)
        {
            DiscordApiData data = channelsHttp.BulkDeleteMessages(Id, messageIds);
            return data.IsNull;
        }

        /// <summary>
        /// Causes the current authenticated user to appear as typing in this channel.
        /// </summary>
        /// <returns>Returns whether the operation was successful.</returns>
        public bool TriggerTypingIndicator()
        {
            DiscordApiData data = channelsHttp.TriggerTypingIndicator(Id);
            return data.IsNull;
        }

        /// <summary>
        /// Gets a list of all pinned messages in this channel.
        /// </summary>
        public IList<DiscordMessage> GetPinnedMessages()
        {
            DiscordApiData messagesArray = channelsHttp.GetPinnedMessages(Id);
            DiscordMessage[] messages = new DiscordMessage[messagesArray.Values.Count];
            
            for (int i = 0; i < messages.Length; i++)
            {
                DiscordMessage message = new DiscordMessage(shard);
                message.Update(messagesArray.Values[i]);

                messages[i] = message;
            }

            return messages;
        }

        /// <summary>
        /// Gets a message in this channel.
        /// </summary>
        public DiscordMessage GetMessage(Snowflake messageId)
        {
            DiscordApiData data = channelsHttp.GetMessage(Id, messageId);
            DiscordMessage message = new DiscordMessage(shard);
            message.Update(data);

            return message;
        }

        /// <summary>
        /// Gets a list of messages in this channel.
        /// </summary>
        /// <param name="baseMessageId">The message id the list will start at (is not included in the final list).</param>
        /// <param name="limit">Maximum number of messages to be returned.</param>
        /// <param name="getStrategy">The way messages will be located based on the <paramref name="baseMessageId"/>.</param>
        public IList<DiscordMessage> GetMessages(Snowflake? baseMessageId = null, int? limit = null, 
            DiscordMessageGetStrategy getStrategy = DiscordMessageGetStrategy.Before)
        {
            DiscordApiData messagesArray = channelsHttp.GetMessages(Id, baseMessageId, limit, getStrategy);
            DiscordMessage[] messages = new DiscordMessage[messagesArray.Values.Count];

            for (int i = 0; i < messages.Length; i++)
            {
                DiscordMessage message = new DiscordMessage(shard);
                message.Update(messagesArray.Values[i]);

                messages[i] = message;
            }

            return messages;
        }
        
        internal override void Update(DiscordApiData data)
        {
            base.Update(data);

            DiscordApiData recipientData = data.Get("recipient");
            if (recipientData != null)
            {
                Snowflake recipientId = recipientData.GetSnowflake("id").Value;
                Recipient = shard.Users.Edit(recipientId, () => new DiscordUser(), user => user.Update(recipientData));
            }

            LastMessageId = data.GetSnowflake("last_message_id") ?? LastMessageId;
        }

        public override string ToString()
        {
            return $"DM Channel: {Recipient}";
        }
    }
}