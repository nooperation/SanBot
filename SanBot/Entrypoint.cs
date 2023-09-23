namespace SanBot
{
    public class Entrypoint
    {
        private static async Task Main(string[] args)
        {
            var bot = new Bot();
            await bot.Start();
        }
    }
}
