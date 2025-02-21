/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core.Libraries;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Suspicious Code Unlock", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class SuspiciousCodeUnlock : RustPlugin
    {
        #region Fields

        private static SuspiciousCodeUnlock _plugin;
        private static Configuration _config;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Discord Webhook Url")]
            public string DiscordWebhookUrl { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DiscordWebhookUrl = ""
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnCodeEntered(CodeLock codeLock, BasePlayer player, string enteredCode)
        {
            if (codeLock == null || player == null)
                return;

            if (codeLock.code != enteredCode)
                return;

            if (player.userID == codeLock.OwnerID)
                return;

            if (PlayerUtil.AreTeammates(codeLock.OwnerID, player.userID))
                return;

            if (player.IsBuildingAuthed())
                return;

            AlertOnlineAdmins(codeLock, player);
            SendDiscordAlert(codeLock, player);
        }

        #endregion Oxide Hooks

        #region Notifications

        private void AlertOnlineAdmins(CodeLock codeLock, BasePlayer unauthorizedPlayer)
        {
            string location = MapHelper.PositionToString(codeLock.transform.position);
            string unlockedEntityName = GetUnlockedEntityName(codeLock);

            BasePlayer owner = PlayerUtil.FindById(codeLock.OwnerID);
            string ownerName = "unknown";
            if (owner != null)
            {
                ownerName = owner.displayName;
            }

            foreach (BasePlayer admin in BasePlayer.activePlayerList)
            {
                if (admin != null && PermissionUtil.HasPermission(admin, PermissionUtil.ADMIN))
                {
                    MessagePlayer(admin, Lang.ChatUnlockAlert, unauthorizedPlayer.displayName, unlockedEntityName, location, ownerName);
                }
            }
        }

        private void SendDiscordAlert(CodeLock codeLock, BasePlayer unauthorizedPlayer)
        {
            if (string.IsNullOrEmpty(_config.DiscordWebhookUrl))
                return;

            string location = MapHelper.PositionToString(codeLock.transform.position);
            string unlockedEntityName = GetUnlockedEntityName(codeLock);

            BasePlayer owner = PlayerUtil.FindById(codeLock.OwnerID);
            string ownerName = "unknown";
            string ownerId = "0";
            if (owner != null)
            {
                ownerName = owner.displayName;
                ownerId = owner.UserIDString;
            }

            string messageTemplate = lang.GetMessage(Lang.DiscordUnlockAlert, this);
            string message = string.Format(messageTemplate, unauthorizedPlayer.displayName, unauthorizedPlayer.UserIDString, unlockedEntityName, location, ownerName, ownerId);

            var postData = new { content = message };
            string json = JsonConvert.SerializeObject(postData);

            webrequest.Enqueue(
                _config.DiscordWebhookUrl,
                json,
                (code, response) =>
                {
                    if (code < 200 || code >= 300)
                        PrintWarning($"Failed to send Discord alert: Code: {code}, Response: {response}");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string> { { "Content-Type", "application/json" } }
            );
        }

        #endregion Notifications

        #region Helper Functions

        private string GetUnlockedEntityName(CodeLock codeLock)
        {
            BaseEntity parentEntity = codeLock.GetParentEntity();

            if (parentEntity != null)
                return parentEntity.ShortPrefabName;

            return "unknown";
        }

        #endregion Helper Functions

        #region Permissions

        private static class PermissionUtil
        {
            public const string ADMIN = "suspiciouscodeunlock.admin";
            private static readonly List<string> _permissions = new List<string>
            {
                ADMIN,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Helper Classes

        public static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static RelationshipManager.PlayerTeam GetTeam(ulong playerId)
            {
                if (RelationshipManager.ServerInstance == null)
                    return null;

                return RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            }

            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                var team = GetTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }
        }

        #endregion Helper Classes

        #region Localization

        private class Lang
        {
            public const string ChatUnlockAlert = "ChatUnlockAlert";
            public const string DiscordUnlockAlert = "DiscordUnlockAlert";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ChatUnlockAlert] = "Suspicious code unlock: {0} unlocked a {1} at {2} owned by {3}.",
                [Lang.DiscordUnlockAlert] = "Suspicious code unlock: {0} ({1}) unlocked a {2} at {3} owned by {4} ({5})."
            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        #endregion Localization
    }
}