using System.Collections.Frozen;
using System.Text;
using Arcadia.EA;
using Arcadia.Storage;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Discord;

public class StatusService(ILogger<StatusService> logger, ConnectionManager sharedCache, IOptions<DiscordSettings> config, StatsStorage stats)
{
    private const string messageIdFile = "./messageId";
    private const string assetsUrlBase = "https://raw.githubusercontent.com/valters-tomsons/arcadia/refs/heads/main/src/server/static/assets/";

    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(10);
    private static readonly StringBuilder PlayersStringBuilder = new();

    private readonly ILogger<StatusService> _logger = logger;
    private readonly ConnectionManager _sharedCache = sharedCache;
    private readonly IOptions<DiscordSettings> _config = config;
    private readonly StatsStorage _stats = stats;

    private readonly List<(IMessageChannel Channel, ulong StatusId)> _channelStatus = [];
    private readonly Dictionary<ulong, List<(long GID, ulong MessageId)>> _channelsGameMessage = [];

    public async Task Execute(DiscordSocketClient client, CancellationToken ct)
    {
        while (client.ConnectionState != ConnectionState.Connected)
        {
            await Task.Delay(1000, ct);
        }

        await InitializeChannels(client);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (client.ConnectionState != ConnectionState.Connected)
                {
                    _logger.LogWarning("Discord connection lost, delaying status update!");
                    await Task.Delay(5000, ct);
                    continue;
                }

                await ProcessOnslaughtStats(client);

                await UpdateGameStatus();
                await Task.Delay(PeriodicUpdateInterval, ct);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to update status!");
            }
        }
    }

    public async Task Shutdown()
    {
        foreach (var (channel, statusId) in _channelStatus)
        {
            try
            {
                await channel.ModifyMessageAsync(statusId, x =>
                {
                    x.Content = "Server offline!";
                    x.Embeds = null;
                });

                var gameMessages = _channelsGameMessage.GetValueOrDefault(channel.Id);
                if (gameMessages is null) continue;

                foreach (var (_, gameMessageId) in gameMessages)
                {
                    await channel.DeleteMessageAsync(gameMessageId);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to notify channel about shutdown!");
            }
        }

        _channelStatus.Clear();
        _channelsGameMessage.Clear();
    }

    private async Task InitializeChannels(DiscordSocketClient client)
    {
        var channels = _config.Value.Channels;
        var cachedIds = await GetCachedStatusMessageIds();
        var flushCache = false;

        foreach (var channelId in channels)
        {
            if (await client.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                _logger.LogError("Failed to open channel: {channelId}", channelId);
                continue;
            }

            var cacheHit = cachedIds.TryGetValue(channel.Id, out var messageId);
            var statusMsg = cacheHit ? await channel.GetMessageAsync(messageId) : await channel.SendMessageAsync("Initializing status...");
            if (statusMsg is null)
            {
                if (cacheHit)
                {
                    _logger.LogWarning("Cached message no longer exists, must manually remove cache line {channelId}:{messageId}", channelId, messageId);
                }

                _logger.LogError("Failed to acquire status message in channel:{channelId}. Messages will not be posted here.", channelId);
                continue;
            }
            else if (!cacheHit)
            {
                flushCache = true;
                cachedIds.Add(channelId, statusMsg.Id);
                _logger.LogInformation("New status created, msgId:{messageId}, chId:{channelId}", statusMsg.Id, channelId);
            }

            await foreach (var batch in channel.GetMessagesAsync())
            {
                foreach (var message in batch)
                {
                    if (message.Id != statusMsg.Id) await message.DeleteAsync();
                }
            }

            _channelStatus.Add((channel, statusMsg.Id));
        }

        if (flushCache)
        {
            await File.WriteAllLinesAsync(messageIdFile, cachedIds.Select(x => $"{x.Key}:{x.Value}"));
        }
    }

    private async Task<Dictionary<ulong, ulong>> GetCachedStatusMessageIds()
    {
        if (!File.Exists(messageIdFile))
        {
            _logger.LogWarning("Status messageId Cache file doesn't exist");
            return [];
        }

        try
        {
            var text = await File.ReadAllLinesAsync(messageIdFile);
            var dict = new Dictionary<ulong, ulong>(text.Length);
            foreach (var line in text)
            {
                var parts = line.Split(':');
                dict.Add(ulong.Parse(parts[0]), ulong.Parse(parts[1]));
            }

            return dict;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read messageId cache file.");
            return [];
        }
    }

    private async Task ProcessOnslaughtStats(DiscordSocketClient client)
    {
        var eb = new EmbedBuilder().WithTitle("Onslaught finished!");

        const int batchSize = 8;
        for (var i = 0; i < batchSize; i++)
        {
            var msg = _stats.DequeueCompletion();
            if (msg is null)
            {
                if (i == 0) return;
                _logger.LogInformation("Finished processing {i} stats messages.", i + 1);
                break;
            }

            var mapInfo = _onslaughtAssets.GetValueOrDefault($"Levels/ONS_MP_{msg.MapKey}");
            if (!mapInfo.HasValue)
            {
                _logger.LogError("Unknown onslaught map key '{MapKey}', not submitting stat {BatchIdx}!", msg.MapKey, i);
                continue;
            }

            var gt = msg.GameTime;
            var message = $"Finished {mapInfo?.Display} on {msg.Difficulty} in {gt.Hours} hours, {gt.Minutes} minutes and {gt.Seconds} seconds".Replace(" 0 hours, ", " ");

            eb.AddField(msg.PlayerName, message);
        }

        if (await client.GetChannelAsync(_config.Value.OnslaughtStatsChannel) is not IMessageChannel channel)
        {
            _logger.LogError("Failed to open status channel: {channelId}", _config.Value.OnslaughtStatsChannel);
            return;
        }

        await channel.SendMessageAsync("\n", embed: eb.Build());
        _logger.LogInformation("New stats batch posted");
    }

    private async Task UpdateGameStatus()
    {
        var content = BuildGameStatusContent();

        foreach (var (channel, statusId) in _channelStatus)
        {
            try
            {
                await channel.ModifyMessageAsync(statusId, x =>
                {
                    x.Content = "\n";
                    x.Embed = new EmbedBuilder()
                            .WithTitle("Arcadia")
                            .WithDescription(content.StatusMessage)
                            .Build();
                });
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update channel status message, reason: {message}", e.Message);
            }

            var gameMessagesInChannel = _channelsGameMessage.GetValueOrDefault(channel.Id);
            if (gameMessagesInChannel is null)
            {
                gameMessagesInChannel = new(content.Games.Length);
                _channelsGameMessage.Add(channel.Id, gameMessagesInChannel);
            }
            else
            {
                var gidsToRemove = new List<long>(3);
                foreach (var postedGame in gameMessagesInChannel)
                {
                    try
                    {
                        if (content.Games.Any(x => x.GID == postedGame.GID)) continue;
                        _logger.LogDebug("Removing game listing, GID:{GID}", postedGame.GID);

                        gidsToRemove.Add(postedGame.GID);
                        await channel.DeleteMessageAsync(postedGame.MessageId);
                    }
                    catch (HttpException e)
                    {
                        _logger.LogError(e, "Failed to delete game server message, reason: {message}", e.Message);
                    }
                }

                gameMessagesInChannel.RemoveAll(x => gidsToRemove.Contains(x.GID));
            }

            try
            {
                for (var i = 0; i < content.Games.Length; i++)
                {
                    var game = content.Games[i];
                    var postedMsg = gameMessagesInChannel.FirstOrDefault(x => game.GID == x.GID);
                    if (postedMsg == default)
                    {
                        var gameMessage = await channel.SendMessageAsync("\n", embed: game.Embed);
                        gameMessagesInChannel.Add((game.GID, gameMessage.Id));
                        _logger.LogDebug("Server listing added, GID:{GID}", game.GID);
                    }
                    else
                    {
                        await channel.ModifyMessageAsync(postedMsg.MessageId, x =>
                        {
                            x.Content = "\n";
                            x.Embed = game.Embed;
                        });

                        _logger.LogDebug("Server listing updated, GID:{GID}", game.GID);
                    }
                }
            }
            catch (HttpException e)
            {
                _logger.LogError(e, "Failed to update game server messages, reason: {message}", e.Message);
            }
        }
    }

    private (string StatusMessage, (long GID, Embed Embed)[] Games) BuildGameStatusContent()
    {
        var hosts = _sharedCache.GetAllServersInternal();

        var gidEmbeds = new (long GID, Embed Embed)[hosts.Length];
        for (var i = 0; i < hosts.Length; i++)
        {
            var server = hosts[i];
            if (!server.CanJoin)
            {
                gidEmbeds[i].GID = 0;
                continue;
            }

            try
            {
                var partitionId = server.PartitionId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()?.ToUpperInvariant()
                    ?? throw new("Cannot find PartitionId");

                gidEmbeds[i] = partitionId switch
                {
                    "BFBC2" => BuildBFBC2Status(server),
                    // "AO3" => BuildAO3Status(server),
                    "MERCS2" => BuildMercs2Status(server),
                    "LOTR" => BuildLOTRStatus(server),
                    // "GODFATHER2" =>
                    "MOHAIR" => BuildMOHStatus(server),
                    _ => throw new($"No game status builder for '{server.PartitionId}'")
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to build server status embedding: {Message}", e.Message);
            }
        }

        var embeds = gidEmbeds.Where(x => x.GID != 0).ToArray();

        const string statusFormat = "**{0}** ongoing game{1}";
        var gameCount = embeds.Length;
        var statusEnd = gameCount == 0 ? "s. 😞" : gameCount > 1 ? "s! 🔥" : "! ⭐";
        var statusMsg = string.Format(statusFormat, gameCount, statusEnd);

        return (statusMsg, embeds);
    }

    private static string GetPlayerCountString(GameServerListing server)
    {
        var maxPlayers = server.Data["MAX-PLAYERS"];

        if (server.ConnectedPlayers.IsEmpty)
        {
            return $"0/{maxPlayers}";
        }

        PlayersStringBuilder.Clear();
        PlayersStringBuilder
            .Append(server.ConnectedPlayers.Count)
            .Append('/')
            .Append(maxPlayers)
            .Append(" | ")
            .AppendJoin(", ", server.ConnectedPlayers.Select(x => x.Value.NAME));

        return PlayersStringBuilder.ToString();
    }

    private static readonly FrozenDictionary<string, (string Display, string Asset)?> _onslaughtAssets = new Dictionary<string, (string Display, string Asset)?>
    {
        { "Levels/ONS_MP_002", ("Valparaiso", "BC2_Valparaiso.jpg") },
        { "Levels/ONS_MP_004", ("Isla Inocentes", "BC2_Isla_Inocentes.jpg") },
        { "Levels/ONS_MP_005", ("Atacama Desert", "BC2_Atacama_Desert.jpg") },
        { "Levels/ONS_MP_008", ("Nelson Bay", "BC2_Nelson_Bay.jpg") },
    }.ToFrozenDictionary();

    private static (long GID, Embed Embed) BuildBFBC2Status(GameServerListing server)
    {
        var serverName = $"**{server.NAME.Replace("P2P-", string.Empty)}**";
        var gamemode = server.Data.GetValueOrDefault("B-U-gamemode") ?? "`N/A`";

        var eb = new EmbedBuilder()
            .WithTitle($"{serverName} ({gamemode})")
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        var difficulty = server.Data.GetValueOrDefault("B-U-difficulty");
        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            eb.AddField("Difficulty", difficulty);
        }

        var levelName = server.Data.GetValueOrDefault("B-U-level");
        if (!string.IsNullOrWhiteSpace(levelName))
        {
            var mapInfo = _onslaughtAssets.GetValueOrDefault(levelName);
            if (mapInfo.HasValue)
            {
                eb.AddField("Level", mapInfo?.Display);
                eb.WithImageUrl(string.Concat(assetsUrlBase, mapInfo?.Asset));
            }
        }

        return (server.GID, eb.Build());
    }

    private static (long GID, Embed Embed) BuildAO3Status(GameServerListing server)
    {
        var serverName = $"**{server.NAME}**";
        var gamemode = server.Data.GetValueOrDefault("B-U-Mode") ?? string.Empty;
        var level = server.Data.GetValueOrDefault("B-U-Map") ?? "`N/A`";
        var playlist = server.Data.GetValueOrDefault("B-U-MapPlaylist") ?? "`N/A`";

        var eb = new EmbedBuilder()
            .WithTitle($"{serverName} (AO3{gamemode})")
            .AddField("Level", level)
            .AddField("Playlist", playlist)
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        return (server.GID, eb.Build());
    }

    private static (long GID, Embed Embed) BuildMercs2Status(GameServerListing server)
    {
        var eb = new EmbedBuilder()
            .WithTitle($"Mercenaries 2")
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        var friendlyFire = server.Data["B-U-FriendlyFire"];
        if (!string.IsNullOrWhiteSpace(friendlyFire))
        {
            eb.AddField("Friendly Fire", friendlyFire == "1" ? "Yes" : "No");
        }

        var mission = server.Data["B-U-Mission"];
        if (!string.IsNullOrWhiteSpace(mission))
        {
            eb.AddField("Mission", mission);
        }

        return (server.GID, eb.Build());
    }

    private static (long GID, Embed Embed) BuildLOTRStatus(GameServerListing server)
    {
        var eb = new EmbedBuilder()
            .WithTitle($"Lord of the Rings: Conquest")
            .AddField("Players", GetPlayerCountString(server))
            .WithTimestamp(server.StartedAt);

        return (server.GID, eb.Build());
    }

    private static (long GID, Embed Embed) BuildMOHStatus(GameServerListing server)
    {
        var eb = new EmbedBuilder()
            .WithTitle($"Medal of Honor: Airborne")
            .AddField("Players", GetPlayerCountString(server))
            .AddField("Map", server.Data["B-U-Map"])
            .AddField("Gamemode", server.Data["B-U-GameType"])
            .WithTimestamp(server.StartedAt);

        return (server.GID, eb.Build());
    }
}