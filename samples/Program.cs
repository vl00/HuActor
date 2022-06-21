using HuLyega;
using Samples;

var builder = WebApplication.CreateBuilder(args);

ConfigServices(builder.Services);
void ConfigServices(IServiceCollection services)
{
    services.AddLogging(loggingBuilder => 
    {
        loggingBuilder.AddConsole();
    });

    services.AddHuActorRT();

    services.AddSingleton(typeof(IHuActorProxy), sp =>
    {
        HuActorProxy.Instance = new Common.HuActorProxy1();
        return HuActorProxy.Instance;
    });
}

var app = builder.Build();

//app.MapGet("/", () => "Hello World!");
OnRuning(app.Services);

app.Run();

async void OnRuning(IServiceProvider services)
{
    _ = services.GetService<IHuActorRT>();
    _ = services.GetService<IHuActorProxy>();
    var logf = services.GetService<ILoggerFactory>();
    var log = logf.CreateLogger("samples");

    await HuActorRT.Instance.StartGC();

    try
    {
        await HuActorProxy.Create<IMyActor>(11).Fx1(1000);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "");
    }

    await HuActorRT.Instance.StopGC();
}
