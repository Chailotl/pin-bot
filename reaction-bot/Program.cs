using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReactionBot
{
	public class Config
	{
		public string token;
		public ulong pinChannel;
		public string pinEmoji;
		public int pinAmount;
		public List<ulong> roles;
	}

	class Program
	{
		private DiscordSocketClient client;
		private Config config;

		static Task Main(string[] args) => new Program().MainAsync();

		public async Task MainAsync()
		{
			config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));

			client = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent | GatewayIntents.GuildMembers,
				MessageCacheSize = 50,
				AlwaysDownloadUsers = true
			});
			client.Log += Log;
			client.Ready += Client_Ready;
			client.ReactionAdded += PinMessage;
			client.ReactionAdded += AddRole;
			client.ReactionRemoved += RemoveRole;
			client.SlashCommandExecuted += SlashCommandHandler;

			await client.LoginAsync(TokenType.Bot, config.token);
			await client.StartAsync();

			await Task.Delay(-1);
		}

		private Task Log(LogMessage msg)
		{
			Console.WriteLine(msg.ToString());
			return Task.CompletedTask;
		}

		private async Task Client_Ready()
		{
			// Sets up reactions
			/*IMessageChannel channel = (IMessageChannel)_client.GetChannel(1002206034640769026);

			foreach(ulong id in config.roles)
			{
				IMessage message = await channel.GetMessageAsync(id);

				Console.WriteLine(message.Content);
				Console.WriteLine(message.CleanContent);

				using (StringReader reader = new StringReader(message.Content))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						if (Emoji.TryParse(line.Split(' ')[0], out Emoji emoji))
						{
							await message.AddReactionAsync(emoji);
						}
					}
				}
			}*/

			var pingCommand = new SlashCommandBuilder()
				.WithName("ping")
				.WithDescription("Ping the bot");

			try
			{
				await client.CreateGlobalApplicationCommandAsync(pingCommand.Build());
			}
			catch (HttpException e)
			{
				Console.WriteLine(JsonConvert.SerializeObject(e.Errors, Formatting.Indented));
			}
		}

		private async Task PinMessage(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
		{
			// Ignore bots
			IUser user = await client.GetUserAsync(reaction.UserId);
			if (user.IsBot) { return; }

			// Ignore non-guilds
			IMessageChannel channel = await cachedChannel.GetOrDownloadAsync();
			if (!(channel is IGuildChannel)) { return; }

			// Ignore pin channel
			if (cachedChannel.Id == config.pinChannel) { return; }

			// Get guild-specific pin data
			IMessageChannel pinChannel = (IMessageChannel)await client.GetChannelAsync(config.pinChannel);
			Emoji emoji = Emoji.Parse(config.pinEmoji);
			int amount = config.pinAmount;

			// Pin message if it has enough reaction pins
			IUserMessage message = await cachedMessage.GetOrDownloadAsync();

			if (message.Reactions.TryGetValue(emoji, out ReactionMetadata meta) && meta.ReactionCount >= amount)
			{
				// Check if this message has already been pinned before
				string id = message.Id.ToString();

				int i = pinChannel.GetMessagesAsync(20).FlattenAsync().Result.Where(x => {
					if (x.Embeds.Count == 0) { return false; }
					string url = x.Embeds.First().Url;
					return url != null && url.Contains(id);
				}).Count();

				if (i > 0) { return; }

				// Send response
				EmbedBuilder embedBuilder = new EmbedBuilder()
					.WithAuthor(message.Author.Username, message.Author.GetAvatarUrl(), message.GetJumpUrl())
					.WithDescription(message.Content)
					.WithUrl($"http://msg.id/{id}")
					.WithTimestamp(message.Timestamp);

				// Find any attachements
				if (message.Attachments.Count > 0)
				{
					embedBuilder.ImageUrl = message.Attachments.First().Url;
				}

				await pinChannel.SendMessageAsync(embed: embedBuilder.Build());
			}
		}

		private async Task AddRole(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
		{
			ModifyRole(true, cachedMessage, cachedChannel, reaction);
		}

		private async Task RemoveRole(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
		{
			ModifyRole(false, cachedMessage, cachedChannel, reaction);
		}

		private async void ModifyRole(bool addRole, Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
		{
			// Ignore non-guilds
			IMessageChannel channel = await cachedChannel.GetOrDownloadAsync();
			if (!(channel is IGuildChannel)) { return; }

			// Ignore bots
			IGuildUser user = await ((IGuildChannel)channel).Guild.GetUserAsync(reaction.UserId);
			if (user.IsBot) { return; }

			// Check if message has reaction roles
			IUserMessage message = await cachedMessage.GetOrDownloadAsync();

			if (config.roles.Contains(message.Id))
			{
				// Get data
				IEmote emote = reaction.Emote;

				// Check which reaction role should be given
				using (StringReader reader = new StringReader(message.Content))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						// Add corresponding role
						if (line.Contains(emote.Name))
						{
							Regex rg = new Regex(@"<@&(\d+)>");
							Match match = rg.Match(line);
							if (match.Success)
							{
								if (addRole)
								{
									await user.AddRoleAsync(ulong.Parse(match.Groups[1].Value));
								}
								else
								{
									await user.RemoveRoleAsync(ulong.Parse(match.Groups[1].Value));
								}
							}
						}
					}
				}
			}
		}

		private async Task SlashCommandHandler(SocketSlashCommand command)
		{
			switch (command.Data.Name)
			{
				case "ping":
					await command.RespondAsync("Pong!", ephemeral: true);
					break;
			}
		}

		/*private async Task HandleColorCommand(SocketSlashCommand command)
		{
			SocketGuildUser user = (SocketGuildUser)command.User;
			SocketGuild guild = user.Guild;
			string roleName = $"USER-{user.Id}";

			// Get color
			string hex = command.Data.Options.First().Value.ToString();
			hex = hex.StartsWith('#') ? hex.Substring(1) : hex;
			Color color = new Color((uint)int.Parse(hex, System.Globalization.NumberStyles.HexNumber));

			// Find role
			SocketRole role = guild.Roles.Where(x => x.Name == roleName).FirstOrDefault();

			if (role != null)
			{
				// Change role color and assign it if they're missing it
				await role.ModifyAsync(x => x.Color = color);

				if (!user.Roles.Contains(role))
				{
					await user.AddRoleAsync(role);
				}
			}
			else
			{
				// Create and assign role
				await user.AddRoleAsync(await guild.CreateRoleAsync(roleName, color: color, isMentionable: false));
			}

			// Send response
			EmbedBuilder embedBuilder = new EmbedBuilder()
				.WithDescription($"Your role color was changed to **{'#' + hex}**")
				.WithColor(color);

			await command.RespondAsync(embed: embedBuilder.Build(), ephemeral: true);
		}*/
	}
}
