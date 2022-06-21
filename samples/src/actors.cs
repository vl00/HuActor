using Common;
using HuLyega;

namespace Samples
{
    public interface IMyActor : IHuActor
    {
        Task Fx1(int i);
        Task<int> Fx2(CancellationToken cancellation);
        Task<int> Fx3();
    }

    [HuActorAttr(nameof(MyActor))]
    public partial class MyActor : BaseHuActor, IMyActor
    {
        [rg.DInject] IConfiguration config;

        public async Task Fx1(int i)
        {
            Log.LogInformation($"actor={ActorId} calling {nameof(Fx1)} Args i={i}");
            await Task.Delay(i).ConfigureAwait(false);
            Log.LogInformation($"actor={ActorId} called {nameof(Fx1)} Args i={i}");
        }

        public async Task<int> Fx2(CancellationToken cancellation)
        {
            await Task.Delay(1000, cancellation);
            return 2;
        }

        public async Task<int> Fx3()
        {
            await Task.Delay(1000);
            Log.LogInformation($"actor={ActorId} called {nameof(Fx3)}");
            return 3;
        }

        protected override async Task OnUnload_Core()
        {
            await Task.Delay(300).ConfigureAwait(false);
            //Log.LogDebug($"actor={ActorId} called {nameof(OnUnload)} !!!");
        }
    }

    [HuActorAttr(nameof(MyActor1))]    
    public partial class MyActor1 : MyActor
    {
        [rg.DInject] IConfiguration config;

        public async Task F1x1(int i)
        {
            await Task.Delay(500).ConfigureAwait(false);
            Log.LogInformation($"actor={ActorId} called {nameof(F1x1)} Args i={i}");
        }
    }
}
