using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
    public interface ICallMethodCtx : IHuActorMessage
    {
        string Method { get; }

        object Result { get; set; }

        object Args(string name);
        T Args<T>(string name);
        bool Args(string name, out object value);
        bool Args<T>(string name, out T value);

        object Args(int i);
        T Args<T>(int i);
        bool Args(int i, out object value);
        bool Args<T>(int i, out T value);
    }

    public class HuCallMethodCtx : ICallMethodCtx
    {
        public HuCallMethodCtx(string method, params (string, object)[] args)
        {
            Method = method;
            this.args = args?.ToDictionary(_ => _.Item1, _ => _.Item2);
        }

        public HuCallMethodCtx(string method, IDictionary<string, object> args)
        {
            Method = method;
            this.args = args;
        }

        readonly IDictionary<string, object> args;

        public string Method { get; }

        public object Result { get; set; }

        public object Args(int i) => args?.ElementAtOrDefault(i).Value;
        public T Args<T>(int i) => (T)Args(i);

        public object Args(string name) => args != null && args.TryGetValue(name, out var r) ? r : null;
        public T Args<T>(string name) => (T)Args(name);

        public bool Args(string name, out object value)
        {
            value = null;
            return args != null && args.TryGetValue(name, out value);
        }

        public bool Args<T>(string name, out T value)
        {
            var b = Args(name, out var val);
            value = !b ? default : val is T v ? v : throw new InvalidCastException($"args name='{name}' cannot cast to type='{typeof(T)}'");
            return b;
        }

        public bool Args(int i, out object value)
        {
            value = Args(i);
            return value != null;
        }

        public bool Args<T>(int i, out T value)
        {
            var b = Args(i, out var val);
            value = !b ? default : val is T v ? v : throw new InvalidCastException($"args i={i} cannot cast to type='{typeof(T)}'");
            return b;
        }
    }

    public static class HuActorCallMethodCtxExtension
    {
        public static async Task<ICallMethodCtx> InvokeAsync(this IHuActorRT actorRT, HuActorId actorId, ICallMethodCtx medCtx, CancellationToken cancellation = default)
        {
            await actorRT.HandleMessageAsync(actorId, medCtx, cancellation);
            return medCtx;
        }

        public static Task<ICallMethodCtx> InvokeAsync(this IHuActorRT actorRT, object id, string actorName, ICallMethodCtx medCtx, CancellationToken cancellation = default)
        {
            return InvokeAsync(actorRT, new HuActorId(id) { ActorName = actorName }, medCtx, cancellation);
        }

        public static async Task<object> InvokeAsync(this IHuActorRT actorRT, object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            var msg = await actorRT.InvokeAsync(new HuActorId(id) { ActorName = actorName }, new HuCallMethodCtx(method, args), cancellation);
            return msg.Result;
        }

        public static async Task<T> InvokeAsync<T>(this IHuActorRT actorRT, object id, string actorName, string method, IDictionary<string, object> args, CancellationToken cancellation = default)
        {
            return (T)(await InvokeAsync(actorRT, id, actorName, method, args, cancellation));
        }
    }
}