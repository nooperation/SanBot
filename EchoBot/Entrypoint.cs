namespace EchoBot
{
    public class Entrypoint
    {
        private static async Task Main(string[] args)
        {
            var bot = new EchoBot();
            await bot.Start();
        }
    }
}
