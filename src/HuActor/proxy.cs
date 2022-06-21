using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
    public interface IHuActorProxy
    {
        TActorInterface Create<TActorInterface>(HuActorId actorId) 
            where TActorInterface : IHuActor;

        object Create(Type actorInterface, HuActorId actorId);
    }

    public class HuActorProxy
    {
        public static IHuActorProxy Instance;

        /// <summary>
        /// eg <code>HuActorProxy.Create&lt;<typeparamref name="TActorInterface"/>&gt;(new(id, actorName));</code>
        /// </summary>
        public static TActorInterface Create<TActorInterface>(HuActorId actorId)
            where TActorInterface : IHuActor
        {
            return Instance.Create<TActorInterface>(actorId);
        }

        public static object Create(Type actorInterface, HuActorId actorId)
        {
            return Instance.Create(actorInterface, actorId);
        }

        /// <summary>
        /// same as <code>HuActorProxy.Create&lt;<typeparamref name="TActorInterface"/>&gt;(new(id, typeof(<typeparamref name="TActorInterface"/>).Name.TrimStart('I')));</code>
        /// </summary>
        public static TActorInterface Create<TActorInterface>(object id)
            where TActorInterface : IHuActor
        {
            return Instance.Create<TActorInterface>(new(id, typeof(TActorInterface).Name.TrimStart('I')));
        }

        public static Task<T> InvokeAsync<T>(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            if (Instance is HuActorProxy hu) return hu.InvokeMethodImplAsync<T>(id, actorName, method, args, cancellation);
            throw new NotSupportedException("Instance is not HuActorProxy hu");
        }

        public static Task InvokeAsync(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            if (Instance is HuActorProxy hu) return hu.InvokeMethodImplAsync(id, actorName, method, args, cancellation);
            throw new NotSupportedException("Instance is not HuActorProxy hu");
        }

        protected virtual Task<T> InvokeMethodImplAsync<T>(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            throw new NotSupportedException("need override");
        }

        protected virtual Task InvokeMethodImplAsync(object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            throw new NotImplementedException("need override");
        }
    }
}