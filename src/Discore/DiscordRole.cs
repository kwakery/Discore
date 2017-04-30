﻿using Discore.Http;
using System;
using System.Threading.Tasks;

namespace Discore
{
    /// <summary>
    /// Roles represent a set of permissions attached to a group of users.
    /// </summary>
    public sealed class DiscordRole : DiscordIdObject
    {
        /// <summary>
        /// Gets the id of the guild this role is for.
        /// </summary>
        public Snowflake GuildId { get; }
        /// <summary>
        /// Gets the name of this role.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Gets the displayed color of this role.
        /// </summary>
        public DiscordColor Color { get; }
        /// <summary>
        /// Gets whether this role is pinned in the user list of a guild.
        /// </summary>
        public bool IsHoisted { get; }
        /// <summary>
        /// Gets the ordering position of this role.
        /// </summary>
        public int Position { get; }
        /// <summary>
        /// Gets the permissions specified by this role.
        /// </summary>
        public DiscordPermission Permissions { get; }
        /// <summary>
        /// Gets whether this role is managed.
        /// </summary>
        public bool IsManaged { get; }
        /// <summary>
        /// Gets whether this role is mentionable.
        /// </summary>
        public bool IsMentionable { get; }

        DiscordHttpGuildEndpoint guildsHttp;

        internal DiscordRole(IDiscordApplication app, Snowflake guildId, DiscordApiData data)
            : base(data)
        {
            guildsHttp = app.HttpApi.Guilds;

            GuildId = guildId;

            Name = data.GetString("name");
            IsHoisted = data.GetBoolean("hoist").Value;
            Position = data.GetInteger("position").Value;
            IsManaged = data.GetBoolean("managed").Value;
            IsMentionable = data.GetBoolean("mentionable").Value;

            int color = data.GetInteger("color").Value;
            Color = DiscordColor.FromHexadecimal(color);

            long permissions = data.GetInt64("permissions").Value;
            Permissions = (DiscordPermission)permissions;
        }

        /// <summary>
        /// Modifies the settings of this role.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="DiscordHttpApiException"></exception>
        public Task<DiscordRole> Modify(ModifyRoleParameters parameters)
        {
            return guildsHttp.ModifyRole(GuildId, Id, parameters);
        }

        /// <summary>
        /// Deletes this role.
        /// </summary>
        /// <exception cref="DiscordHttpApiException"></exception>
        public Task<bool> Delete()
        {
            return guildsHttp.DeleteRole(GuildId, Id);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
