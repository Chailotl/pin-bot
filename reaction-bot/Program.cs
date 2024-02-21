using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReactionBot
{
	public class Config
	{
		public string token;
		public ulong pinChannel;
		public string pinEmoji;
		public int pinAmount;
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
			client.ReactionAdded += PinMessage;

			await client.LoginAsync(TokenType.Bot, config.token);
			await client.StartAsync();

			await Task.Delay(-1);
		}

		private Task Log(LogMessage msg)
		{
			Console.WriteLine(msg.ToString());
			return Task.CompletedTask;
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
	}
}
