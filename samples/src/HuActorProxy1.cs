using HuLyega;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Common
{
    internal partial class HuActorProxy1 : HuActorProxy, IHuActorProxy
    {
        public readonly HuActorId ActorId;

        public HuActorProxy1() { }

        private HuActorProxy1(HuActorId actorId)
        {
            ActorId = actorId;
        }

        public new TActorInterface Create<TActorInterface>(HuActorId actorId)
            where TActorInterface : IHuActor
        {
            var o = new HuActorProxy1(actorId);
            return Unsafe.As<HuActorProxy1, TActorInterface>(ref o);
        }

        public new object Create(Type actorInterface, HuActorId actorId)
        {
            return new HuActorProxy1(actorId);
        }

        protected override Task InvokeMethodImplAsync(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            return HuActorRT.Instance.InvokeAsync(id, actorName, method, args, cancellation);
        }

        protected override Task<T> InvokeMethodImplAsync<T>(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            return HuActorRT.Instance.InvokeAsync<T>(id, actorName, method, args, cancellation);
        }
    }
}