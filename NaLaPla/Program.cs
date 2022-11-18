namespace NaLaPla
{
    using Microsoft.Extensions.Configuration;

    class Program {

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            var root = Directory.GetCurrentDirectory();
            var dotenv = Path.Combine(root, ".env");
            DotEnv.Load(dotenv);

            var config =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

            await CommandLineInterface.GetBasePlanDescription();
        }
    }
}