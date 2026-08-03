using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using static Arcadia.Discord.Constants;

namespace Arcadia.Discord;

public sealed class NotificationService(ILogger<NotificationService> logger)
{
    private readonly ILogger<NotificationService> _logger = logger;

    private const string CommandName = "setup-notifications";
    private const string ChannelName = "game-notifications";
    private const string CustomIdPrefix = "notify-role:";
    private const string SignUpText = "**Game notifications** — click a button to subscribe or unsubscribe:";

    public async Task Initialize(DiscordSocketClient client)
    {
        var command = new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Create the game notifications channel, roles, and sign-up message")
            .WithDefaultMemberPermissions(GuildPermission.Administrator)
            .Build();

        foreach (var guild in client.Guilds)
        {
            await guild.CreateApplicationCommandAsync(command);
        }
    }

    public Task OnSlashCommandExecuted(SocketSlashCommand command)
    {
        if (command.CommandName != CommandName) return Task.CompletedTask;

        _ = Task.Run(() => SetupNotifications(command));
        return Task.CompletedTask;
    }

    public Task OnButtonExecuted(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith(CustomIdPrefix)) return Task.CompletedTask;

        _ = Task.Run(() => ToggleRole(component));
        return Task.CompletedTask;
    }

    private async Task SetupNotifications(SocketSlashCommand command)
    {
        try
        {
            await command.DeferAsync(ephemeral: true);

            if (command.Channel is not SocketGuildChannel { Guild: var guild })
            {
                await command.FollowupAsync("Run this inside the server.", ephemeral: true);
                return;
            }

            ITextChannel? channel = guild.TextChannels.FirstOrDefault(c => c.Name == ChannelName);
            if (channel is null)
            {
                channel = await guild.CreateTextChannelAsync(ChannelName);
                await channel.AddPermissionOverwriteAsync(guild.EveryoneRole,
                    new OverwritePermissions(sendMessages: PermValue.Deny, addReactions: PermValue.Deny));
            }

            var buttons = new ComponentBuilder();
            foreach (var row in Roles)
            {
                if (guild.Roles.All(r => r.Name != row.DisplayName))
                {
                    await guild.CreateRoleAsync(row.DisplayName, isMentionable: true);
                }

                buttons.WithButton(row.DisplayName, $"{CustomIdPrefix}{row.Id}", ButtonStyle.Secondary, emote: ParseEmote(row.Emote));
            }

            var built = buttons.Build();
            var existing = (await channel.GetMessagesAsync(50).FlattenAsync())
                .OfType<IUserMessage>()
                .FirstOrDefault(m => m.Author.Id == guild.CurrentUser.Id && m.Components.Count > 0);

            if (existing is not null)
            {
                await existing.ModifyAsync(m => { m.Content = SignUpText; m.Components = built; });
            }
            else
            {
                await channel.SendMessageAsync(SignUpText, components: built);
            }

            await command.FollowupAsync($"Notification sign-up is ready in <#{channel.Id}>.", ephemeral: true);
            _logger.LogInformation("[Roles] Setup run by {Username} ({UserId})", command.User.Username, command.User.Id);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[Roles] Setup failed: {Message}", e.Message);
            await command.FollowupAsync("Setup failed, check the logs.", ephemeral: true);
        }
    }

    private async Task ToggleRole(SocketMessageComponent component)
    {
        try
        {
            await component.DeferAsync(ephemeral: true);

            if (!Enum.TryParse<GameRole>(component.Data.CustomId.AsSpan(CustomIdPrefix.Length), out var id)) return;
            if (Roles.FirstOrDefault(r => r.Id == id) is not { } row) return;
            if (component.User is not SocketGuildUser user) return;

            var role = user.Guild.Roles.FirstOrDefault(r => r.Name == row.DisplayName);
            if (role is null)
            {
                await component.FollowupAsync($"The **{row.DisplayName}** role is missing — ask an admin to re-run /{CommandName}.", ephemeral: true);
                return;
            }

            if (user.Roles.Any(r => r.Id == role.Id))
            {
                await user.RemoveRoleAsync(role);
                await component.FollowupAsync($"Unsubscribed from **{row.DisplayName}** notifications.", ephemeral: true);
            }
            else
            {
                await user.AddRoleAsync(role);
                await component.FollowupAsync($"Subscribed to **{row.DisplayName}** notifications!", ephemeral: true);
            }

            _logger.LogInformation("[Roles] Toggled '{Role}' for {Username} ({UserId})", row.Id, user.Username, user.Id);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[Roles] Failed toggling role for user {UserId}: {Message}", component.User.Id, e.Message);
        }
    }

    private static IEmote? ParseEmote(string? s) =>
        string.IsNullOrEmpty(s) ? null
        : Emote.TryParse(s, out var custom) ? custom
        : new Emoji(s);
}