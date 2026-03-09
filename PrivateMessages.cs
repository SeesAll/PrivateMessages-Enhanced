using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("PrivateMessages", "MisterPixie, enhanced by SeesAll", "1.2.1")]
    [Description("Allows users to send private messages to each other")]
    class PrivateMessages : CovalencePlugin
    {
        private const string AllowPermission = "privatemessages.allow";
        private const int MaxHistoryEntries = 5;
        private const string AuditLogDirectoryName = "PrivateMessages";
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1);

        private readonly Dictionary<string, string> pmHistory = new Dictionary<string, string>();
        private readonly Dictionary<string, double> cooldowns = new Dictionary<string, double>();
        private readonly Dictionary<string, ConversationHistory> conversationHistory = new Dictionary<string, ConversationHistory>();

        [PluginReference] private Plugin Ignore, UFilter, BetterChatMute;

        private ConfigData configData;
        private string auditLogDirectoryPath;
        private double nextHistoryPruneAt;

        private class ConversationHistory
        {
            public readonly List<string> Messages = new List<string>();
            public double LastUpdated;
        }

        #region Localization

        private string Lang(string key, string id = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"PMTo", "[#00FFFF]PM to {0}[/#]: {1}"},
                {"PMFrom", "[#00FFFF]PM from {0}[/#]: {1}"},
                {"PlayerNotOnline", "{0} is not online."},
                {"NotOnlineAnymore", "The last person you was talking to is not online anymore."},
                {"NotMessaged", "You haven't messaged anyone or they haven't messaged you."},
                {"IgnoreYou", "[#FF0000]{0} is ignoring you and cant receive your PMs[/#]"},
                {"SelfPM", "You can not send messages to yourself."},
                {"SyntaxR", "Incorrect Syntax use: /r <msg>"},
                {"HistorySyntax", "Incorrect Syntax use: /pmhistory <name>"},
                {"SyntaxPM", "Incorrect Syntax use: /{0} <name> <msg>"},
                {"NotAllowedToChat", "You are not allowed to chat here"},
                {"History", "Your History:\n{0}"},
                {"CooldownMessage", "You will be able to send a private message in {0} seconds"},
                {"NoHistory", "There is not any saved pm history with this player."},
                {"CannotFindUser", "Cannot find this user"},
                {"MultiplePlayersFound", "Multiple players matched '{0}': {1}. Please be more specific."},
                {"CommandDisabled", "This command has been disabled"},
                {"IsMuted", "You are currently muted & cannot send private messages"},
                {"TargetMuted", "This person is muted & cannot receive your private message"},
                {"NoPermission", "You don't have the correct permissions to run this command"},
                {"HistoryPM", "[#00FFFF]{0}[/#]: {1}"},
                {"Logging", "[PM]{0}->{1}:{2}"}
            }, this);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            LoadVariables();

            AddCovalenceCommand(string.IsNullOrEmpty(configData.PmCommand) ? "pm" : configData.PmCommand, nameof(cmdPm));
            AddCovalenceCommand("r", nameof(cmdPmReply));
            AddCovalenceCommand("pmhistory", nameof(cmdPmHistory));
            permission.RegisterPermission(AllowPermission, this);

            auditLogDirectoryPath = Path.Combine("oxide", "logs", AuditLogDirectoryName);
            Directory.CreateDirectory(auditLogDirectoryPath);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            if (player == null)
            {
                return;
            }

            pmHistory.Remove(player.Id);
            cooldowns.Remove(player.Id);
            RemoveStaleReplyTargets(player.Id);
            RemovePlayerConversationHistory(player.Id);
        }

        private void OnNewSave(string filename)
        {
            if (!configData.ClearAuditLogsOnNewSave)
            {
                return;
            }

            ClearAuditLogs();
        }

        #endregion

        #region Commands

        private void cmdPm(IPlayer player, string command, string[] args)
        {
            if (!HasPmPermission(player))
            {
                return;
            }

            if (args == null || args.Length < 2)
            {
                player.Reply(Lang("SyntaxPM", player.Id, configData.PmCommand));
                return;
            }

            string lookupError;
            var target = FindConnectedPlayer(args[0], out lookupError);
            if (target == null)
            {
                player.Reply(string.IsNullOrEmpty(lookupError) ? Lang("PlayerNotOnline", player.Id, args[0]) : lookupError);
                return;
            }

            if (target.Id == player.Id)
            {
                player.Reply(Lang("SelfPM", player.Id));
                return;
            }

            HandlePm(player, target, BuildMessage(args, 1));
        }

        private void cmdPmReply(IPlayer player, string command, string[] args)
        {
            if (!HasPmPermission(player))
            {
                return;
            }

            if (args == null || args.Length == 0)
            {
                player.Reply(Lang("SyntaxR", player.Id));
                return;
            }

            string targetId;
            if (!pmHistory.TryGetValue(player.Id, out targetId))
            {
                player.Reply(Lang("NotMessaged", player.Id));
                return;
            }

            string lookupError;
            var target = FindConnectedPlayer(targetId, out lookupError);
            if (target == null)
            {
                pmHistory.Remove(player.Id);
                player.Reply(Lang("NotOnlineAnymore", player.Id));
                return;
            }

            HandlePm(player, target, BuildMessage(args, 0));
        }

        private void cmdPmHistory(IPlayer player, string command, string[] args)
        {
            if (!configData.EnableHistory)
            {
                player.Reply(Lang("CommandDisabled", player.Id));
                return;
            }

            PruneInactiveConversationHistoryIfNeeded();

            if (args == null || args.Length != 1)
            {
                player.Reply(Lang("HistorySyntax", player.Id));
                return;
            }

            var target = covalence.Players.FindPlayer(args[0]);
            if (target == null)
            {
                player.Reply(Lang("CannotFindUser", player.Id));
                return;
            }

            var history = GetConversationHistory(player.Id, target.Id);
            if (history == null || history.Messages.Count == 0)
            {
                player.Reply(Lang("NoHistory", player.Id));
                return;
            }

            player.Reply(Lang("History", player.Id, string.Join(Environment.NewLine, history.Messages)));
        }

        #endregion

        #region PM Processing

        private void HandlePm(IPlayer sender, IPlayer target, string rawMessage)
        {
            if (!(bool)(Interface.Oxide.CallHook("CanChat", sender) ?? true))
            {
                sender.Reply(Lang("NotAllowedToChat", sender.Id));
                return;
            }

            if (configData.UseBetterChatMute && BetterChatMute != null && CheckMuteStatus(sender, target))
            {
                return;
            }

            if (IsOnCooldown(sender))
            {
                return;
            }

            if (IsIgnored(sender, target))
            {
                return;
            }

            var message = ProcessMessage(rawMessage);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (Interface.Oxide.CallHook("OnPMProcessed", sender, target, message) != null)
            {
                return;
            }

            AddPmHistory(sender.Id, target.Id);

            sender.Reply(Lang("PMTo", sender.Id, target.Name, message));
            target.Reply(Lang("PMFrom", target.Id, sender.Name, message));

            AddHistoryAndLogging(sender, target, message);
        }

        private bool HasPmPermission(IPlayer player)
        {
            if (!configData.UsePermission)
            {
                return true;
            }

            if (player.HasPermission(AllowPermission))
            {
                return true;
            }

            player.Reply(Lang("NoPermission", player.Id));
            return false;
        }

        private void AddHistoryAndLogging(IPlayer initiator, IPlayer target, string message)
        {
            PruneInactiveConversationHistoryIfNeeded();

            if (configData.EnableHistory)
            {
                AddToHistory(initiator.Id, target.Id, Lang("HistoryPM", null, initiator.Name, message));
            }

            if (configData.EnableConsoleLogging)
            {
                Puts(Lang("Logging", null, initiator.Name, target.Name, message));
            }

            if (configData.EnableFileAuditLogging)
            {
                WriteAuditLog(initiator, target, message);
            }
        }

        private void AddPmHistory(string initiatorId, string targetId)
        {
            pmHistory[initiatorId] = targetId;
            pmHistory[targetId] = initiatorId;
        }

        private bool CheckMuteStatus(IPlayer player, IPlayer target)
        {
            var playerMuted = BetterChatMute.CallHook("API_IsMuted", player);
            if (playerMuted is bool && (bool)playerMuted)
            {
                player.Reply(Lang("IsMuted", player.Id));
                return true;
            }

            var targetMuted = BetterChatMute.CallHook("API_IsMuted", target);
            if (targetMuted is bool && (bool)targetMuted)
            {
                player.Reply(Lang("TargetMuted", player.Id));
                return true;
            }

            return false;
        }

        private bool IsIgnored(IPlayer sender, IPlayer target)
        {
            if (!configData.UseIgnore)
            {
                return false;
            }

            var ignored = Ignore?.CallHook("HasIgnored", target.Id, sender.Id);
            if (!(ignored is bool) || !(bool)ignored)
            {
                return false;
            }

            sender.Reply(Lang("IgnoreYou", sender.Id, target.Name));
            return true;
        }

        private bool IsOnCooldown(IPlayer player)
        {
            if (!configData.UseCooldown)
            {
                return false;
            }

            var now = GetTimeStamp();
            double expiresAt;
            if (cooldowns.TryGetValue(player.Id, out expiresAt))
            {
                if (expiresAt > now)
                {
                    player.Reply(Lang("CooldownMessage", player.Id, Math.Round(expiresAt - now, 2)));
                    return true;
                }

                cooldowns.Remove(player.Id);
            }

            cooldowns[player.Id] = now + configData.CooldownTime;
            return false;
        }

        private string ProcessMessage(string message)
        {
            if (configData.UseUFilter && UFilter != null)
            {
                var filtered = UFilter.Call("ProcessText", message);
                if (filtered != null)
                {
                    message = filtered.ToString();
                }
            }

            return RemoveRichText(message);
        }

        private static string BuildMessage(string[] args, int startIndex)
        {
            if (args == null || args.Length <= startIndex)
            {
                return string.Empty;
            }

            return string.Join(" ", args, startIndex, args.Length - startIndex);
        }

        #endregion

        #region Player Lookup

        private IPlayer FindConnectedPlayer(string nameOrIdOrIp, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(nameOrIdOrIp))
            {
                return null;
            }

            IPlayer partialMatch = null;
            var multipleMatches = new List<string>();
            var search = nameOrIdOrIp.Trim();

            foreach (var activePlayer in covalence.Players.Connected)
            {
                if (activePlayer == null)
                {
                    continue;
                }

                if (string.Equals(activePlayer.Id, search, StringComparison.Ordinal) ||
                    string.Equals(activePlayer.Address, search, StringComparison.Ordinal) ||
                    string.Equals(activePlayer.Name, search, StringComparison.OrdinalIgnoreCase))
                {
                    return activePlayer;
                }

                if (string.IsNullOrEmpty(activePlayer.Name) ||
                    activePlayer.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (partialMatch == null)
                {
                    partialMatch = activePlayer;
                    multipleMatches.Add(activePlayer.Name);
                    continue;
                }

                multipleMatches.Add(activePlayer.Name);
            }

            if (multipleMatches.Count > 1)
            {
                errorMessage = Lang("MultiplePlayersFound", null, search, string.Join(", ", multipleMatches));
                return null;
            }

            return partialMatch;
        }

        #endregion

        #region History

        private void AddToHistory(string senderId, string targetId, string message)
        {
            var history = GetOrCreateConversationHistory(senderId, targetId);
            history.LastUpdated = GetTimeStamp();
            history.Messages.Add(message);

            if (history.Messages.Count > MaxHistoryEntries)
            {
                history.Messages.RemoveAt(0);
            }
        }

        private ConversationHistory GetConversationHistory(string senderId, string targetId)
        {
            ConversationHistory history;
            return conversationHistory.TryGetValue(GetConversationKey(senderId, targetId), out history) ? history : null;
        }

        private ConversationHistory GetOrCreateConversationHistory(string senderId, string targetId)
        {
            var key = GetConversationKey(senderId, targetId);

            ConversationHistory history;
            if (!conversationHistory.TryGetValue(key, out history))
            {
                history = new ConversationHistory
                {
                    LastUpdated = GetTimeStamp()
                };
                conversationHistory[key] = history;
            }

            return history;
        }

        private void PruneInactiveConversationHistoryIfNeeded()
        {
            if (!configData.EnableHistory || configData.HistoryPruneMinutes <= 0 || conversationHistory.Count == 0)
            {
                return;
            }

            var now = GetTimeStamp();
            if (now < nextHistoryPruneAt)
            {
                return;
            }

            nextHistoryPruneAt = now + Math.Min(configData.HistoryPruneMinutes * 60.0, 300.0);
            PruneInactiveConversationHistory(now);
        }

        private void PruneInactiveConversationHistory(double now)
        {
            var expirationAge = configData.HistoryPruneMinutes * 60.0;
            var keysToRemove = new List<string>();

            foreach (var entry in conversationHistory)
            {
                if (now - entry.Value.LastUpdated >= expirationAge)
                {
                    keysToRemove.Add(entry.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                conversationHistory.Remove(key);
            }
        }

        private void RemovePlayerConversationHistory(string playerId)
        {
            if (conversationHistory.Count == 0 || string.IsNullOrEmpty(playerId))
            {
                return;
            }

            var keysToRemove = new List<string>();
            foreach (var entry in conversationHistory)
            {
                if (ConversationKeyContainsPlayer(entry.Key, playerId))
                {
                    keysToRemove.Add(entry.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                conversationHistory.Remove(key);
            }
        }

        private static bool ConversationKeyContainsPlayer(string conversationKey, string playerId)
        {
            if (string.IsNullOrEmpty(conversationKey) || string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            var separatorIndex = conversationKey.IndexOf('|');
            if (separatorIndex < 0)
            {
                return false;
            }

            return string.CompareOrdinal(conversationKey, 0, playerId, 0, playerId.Length) == 0 &&
                   (conversationKey.Length == playerId.Length || separatorIndex == playerId.Length)
                   ||
                   separatorIndex + 1 + playerId.Length == conversationKey.Length &&
                   string.CompareOrdinal(conversationKey, separatorIndex + 1, playerId, 0, playerId.Length) == 0;
        }

        private static string GetConversationKey(string playerA, string playerB)
        {
            return string.CompareOrdinal(playerA, playerB) < 0
                ? playerA + "|" + playerB
                : playerB + "|" + playerA;
        }

        #endregion

        #region Audit Logging

        private void WriteAuditLog(IPlayer initiator, IPlayer target, string message)
        {
            try
            {
                var timestamp = DateTime.Now;
                var filePath = Path.Combine(auditLogDirectoryPath, string.Format("privatemessages_{0:yyyy-MM-dd}.log", timestamp));
                var line = string.Format(
                    "[{0:yyyy-MM-dd HH:mm:ss}] {1} ({2}) -> {3} ({4}): {5}",
                    timestamp,
                    SanitizeLogValue(initiator?.Name),
                    SanitizeLogValue(initiator?.Id),
                    SanitizeLogValue(target?.Name),
                    SanitizeLogValue(target?.Id),
                    SanitizeLogValue(message));

                File.AppendAllText(filePath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                PrintWarning("Failed to write PM audit log: {0}", ex.Message);
            }
        }

        private void ClearAuditLogs()
        {
            try
            {
                if (!Directory.Exists(auditLogDirectoryPath))
                {
                    return;
                }

                foreach (var filePath in Directory.GetFiles(auditLogDirectoryPath, "*.log", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Failed to clear PM audit logs: {0}", ex.Message);
            }
        }

        private void RemoveStaleReplyTargets(string playerId)
        {
            var staleReplyTargets = new List<string>();
            foreach (var entry in pmHistory)
            {
                if (entry.Value == playerId)
                {
                    staleReplyTargets.Add(entry.Key);
                }
            }

            foreach (var stalePlayerId in staleReplyTargets)
            {
                pmHistory.Remove(stalePlayerId);
            }
        }

        private static string SanitizeLogValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        #endregion

        #region Utilities

        private static string RemoveRichText(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var output = new char[message.Length];
            var outputIndex = 0;
            var insideTag = false;

            for (var i = 0; i < message.Length; i++)
            {
                var character = message[i];
                if (character == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (character == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                {
                    output[outputIndex++] = character;
                }
            }

            return new string(output, 0, outputIndex);
        }

        private static double GetTimeStamp()
        {
            return (DateTime.UtcNow - UnixEpoch).TotalSeconds;
        }

        #endregion

        #region Configuration

        private class ConfigData
        {
            public bool UseUFilter = false;
            public bool UseIgnore = false;
            public bool UseCooldown = false;
            public bool UseBetterChatMute = false;
            public bool EnableConsoleLogging = false;
            public bool EnableFileAuditLogging = true;
            public bool ClearAuditLogsOnNewSave = false;
            public bool EnableHistory = false;
            public bool UsePermission = false;
            public int CooldownTime = 3;
            public int HistoryPruneMinutes = 60;
            public string PmCommand = "pm";
        }

        private void LoadVariables()
        {
            var configChanged = false;
            configData = new ConfigData
            {
                UseUFilter = GetConfigValue(nameof(ConfigData.UseUFilter), false, ref configChanged),
                UseIgnore = GetConfigValue(nameof(ConfigData.UseIgnore), false, ref configChanged),
                UseCooldown = GetConfigValue(nameof(ConfigData.UseCooldown), false, ref configChanged),
                UseBetterChatMute = GetConfigValue(nameof(ConfigData.UseBetterChatMute), false, ref configChanged),
                EnableConsoleLogging = GetConfigValue(nameof(ConfigData.EnableConsoleLogging), false, ref configChanged),
                EnableFileAuditLogging = GetConfigValue(nameof(ConfigData.EnableFileAuditLogging), true, ref configChanged),
                ClearAuditLogsOnNewSave = GetConfigValue(nameof(ConfigData.ClearAuditLogsOnNewSave), false, ref configChanged),
                EnableHistory = GetConfigValue(nameof(ConfigData.EnableHistory), false, ref configChanged),
                UsePermission = GetConfigValue(nameof(ConfigData.UsePermission), false, ref configChanged),
                CooldownTime = GetConfigValue(nameof(ConfigData.CooldownTime), 3, ref configChanged),
                HistoryPruneMinutes = GetConfigValue(nameof(ConfigData.HistoryPruneMinutes), 60, ref configChanged),
                PmCommand = GetConfigValue(nameof(ConfigData.PmCommand), "pm", ref configChanged)
            };

            if (configData.CooldownTime < 0)
            {
                configData.CooldownTime = 0;
                configChanged = true;
            }

            if (configData.HistoryPruneMinutes < 0)
            {
                configData.HistoryPruneMinutes = 0;
                configChanged = true;
            }

            if (string.IsNullOrEmpty(configData.PmCommand))
            {
                configData.PmCommand = "pm";
                configChanged = true;
            }

            if (configChanged)
            {
                SaveConfig(configData);
            }
        }

        protected override void LoadDefaultConfig()
        {
            SaveConfig(new ConfigData());
        }

        private T GetConfigValue<T>(string key, T defaultValue, ref bool configChanged)
        {
            if (Config[key] == null)
            {
                Config[key] = defaultValue;
                configChanged = true;
                return defaultValue;
            }

            return (T)Convert.ChangeType(Config[key], typeof(T));
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        #endregion
    }
}
