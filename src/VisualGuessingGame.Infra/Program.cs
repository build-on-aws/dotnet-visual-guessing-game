using Amazon.CDK;

namespace VisualGuessingGame.Infra
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new VisualGuessingGameStack(app, "VisualGuessingGameStack", new StackProps
            {
            });
            app.Synth();
        }
    }
}
