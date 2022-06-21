using Common;
using HuLyega;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
    public abstract class BaseHuActor : HuActor
    {
        protected ILogger Log { get; }

        public IServiceProvider Services { get; init; }

        protected BaseHuActor() : base() { }

        public BaseHuActor(HuActorId actorId, IServiceProvider services) : base(actorId)
        {
            Services = services;
            Log = services.GetService<ILoggerFactory>()?.CreateLogger(GetType()) ?? NullLogger.Instance;
            this.DInjectInit(services);
        }

        protected virtual Task OnLoad_Core() => Task.CompletedTask;
        protected virtual Task OnUnload_Core() => Task.CompletedTask;

        public sealed override async Task OnLoad()
        {
            try
            {
                Log.LogDebug($"actor='{ActorId}' calling {nameof(OnLoad)}.");
                await OnLoad_Core();
                Log.LogDebug($"actor='{ActorId}' called {nameof(OnLoad)} ok.");
            }
            catch (Exception ex)
            {
                Log.LogError(ex, $"actor='{ActorId}' called {nameof(OnLoad)} error.");
                throw;
            }
        }

        public sealed override async Task OnUnload()
        {
            try
            {
                Log.LogDebug($"actor='{ActorId}' calling {nameof(OnUnload)}");
                await OnUnload_Core();
                Log.LogDebug($"actor='{ActorId}' called {nameof(OnUnload)} ok.");
            }
            catch (Exception ex)
            {
                Log.LogError(ex, $"actor='{ActorId}' called {nameof(OnUnload)} error.");
                throw;
            }
        }

        protected void UnloadOnIdle()
        {
            var rt = Services.GetService<IHuActorRT>();
            _ = rt.UnloadAsync(ActorId, new CancellationToken(true))
                .ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
