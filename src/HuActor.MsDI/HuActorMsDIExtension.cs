using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{    
    public static class HuActorMsDIExtension
    {
        /*//
        public void ConfigureServices(IServiceCollection services, IConfiguration config)
        {
            services.AddHuActorRT(options => 
            {
                options.TimeSpanForGCPeriod = TimeSpan.FromMinutes(1);
                options.InitializerTypes = ...
            });
        }
        //*/

        public static IServiceCollection AddHuActorRT(this IServiceCollection services)
        {
            services.AddOptions<HuActorRTOptions>();
            services.TryAddTransient(typeof(IHuActorFactory), typeof(HuActorFactory0));
            services.TryAddSingleton(typeof(IHuActorCtrlLock), typeof(HuActorCtrlLock0));
            services.TryAddSingleton(typeof(IHuActorRT), static sp => sp.GetRequiredService<HuActorRT>()); 
            services.TryAddSingleton(typeof(HuActorRT), static sp =>
            {
                var opt = sp.GetService<IOptions<HuActorRTOptions>>().Value;
                var rt = ActivatorUtilities.CreateInstance<HuActorRT>(sp,
                    new Func<IHuActorFactory>(() => sp.GetService<IHuActorFactory>()),
                    opt
                );
                HuActorRT.Instance = rt;
                HuActorRTInitializer.Initialize(rt, opt.InitializerTypes);
                return rt;
            });
            return services;
        }

        public static IServiceCollection AddHuActorRT(this IServiceCollection services, Action<HuActorRTOptions> configAction)
        {
            if (configAction != default) services.AddOptions<HuActorRTOptions>().Configure(configAction);
            return AddHuActorRT(services);
        }
    }

    internal class HuActorFactory0 : IHuActorFactory, IDisposable
    {
        private IServiceScope services;        

        public HuActorFactory0(IServiceScopeFactory factory)
        {
            this.services = factory.CreateScope();            
        }

        public HuActor CreateActor(in HuActorId actorId)
        {
            var rt = (services.ServiceProvider.GetService<IHuActorRT>() as HuActorRT)!;
            var actorInfo = rt?.GetActorTyInformation(actorId.ActorName) ?? throw new ArgumentNullException($"Actor with name='{actorId.ActorName}' not found.");
            var actor = ActivatorUtilities.CreateInstance(services.ServiceProvider, actorInfo.ActorType, actorId) as HuActor;            
            return actor;
        }

        public void Dispose()
        {
            if (services != null)
            {
                services.Dispose();
                services = null;                
            }
        }
    }

    internal class HuActorCtrlLock0 : IHuActorCtrlLock
    {
        public async ValueTask<object> LockAsync(HuActorId actorId, CancellationToken cancellation = default)
        {
            var k = GetHuatrLck(actorId);
            var lck = await HuLyegaLock.LockAsync(k, cancellation);
            return lck;
        }

        public ValueTask UnLockAsync(object lck)
        {
            switch (lck)
            {
                case null:
                    break;
                case IAsyncDisposable ad:
                    return ad.DisposeAsync();
                case IDisposable d:
                    d.Dispose();
                    break;
            }
            return default;
        }

        static string GetHuatrLck(in HuActorId actorId) => $"!!&@Hu{actorId}";
    }
}
