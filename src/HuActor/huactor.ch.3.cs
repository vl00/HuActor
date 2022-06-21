using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
#nullable enable 
    public partial class HuActorRT
    {
        static readonly Dictionary<Type, Handlers> _dictOnHandles = new();

        public static void AddHandler(Func<HuActor, IHuActorMessage, CancellationToken, Task?> handler) => AddHandler<HuActor>(handler);

        public static void AddHandler<T>(Func<T, IHuActorMessage, CancellationToken, Task?> handler) where T : HuActor
        {
            if (handler == default) return;

            Handlers<T> handlers;
            lock (_dictOnHandles)
            {
                if (_dictOnHandles.TryGetValue(typeof(T), out var handlers0) && handlers0 is Handlers<T> handlers0T) handlers = handlers0T;
                else _dictOnHandles[typeof(T)] = handlers = new();
            }

            object _lck = handlers;
            Handlers.AddHandler(ref _lck!, ref handlers._handlers, handler);
		}

        public static void RemoveHandler(Func<HuActor, IHuActorMessage, CancellationToken, Task?> handler) => RemoveHandler<HuActor>(handler);

        public static void RemoveHandler<T>(Func<T, IHuActorMessage, CancellationToken, Task?> handler) where T : HuActor
        {
            if (handler == default) return;

            Handlers<T> handlers;
            lock (_dictOnHandles)
            {
                if (_dictOnHandles.TryGetValue(typeof(T), out var handlers0) && handlers0 is Handlers<T> handlers0T) handlers = handlers0T;
                else return;
            }

            object _lck = handlers;
            Handlers.RemoveHandler(ref _lck!, ref handlers._handlers, handler);
        }

        internal static Task InternalCallOnHandle(HuActor actor, IHuActorMessage message, CancellationToken cancellation)
        {
            if (actor != default && message != default)
            {
                var type = actor.GetType();
                do
                {
                    if (_dictOnHandles.TryGetValue(type!, out var handlers) && handlers != default)
                    {
                        var t = handlers.CallOnHandle(actor, ref message, cancellation);
                        if (t != null) return t;
                    }
                }
                while (null != (type = (type == typeof(HuActor) ? null : typeof(HuActor)))); // [type, typeof(HuActor)]
            }
            throw new NotSupportedException();
        }

        abstract class Handlers
        {
            internal static void AddHandler<T>(ref object? lck, ref T?[]? handlers, T handler) where T : class //, Delegate
            {
                if (handler == default) return;
                lock (EnsureHandlersLock(ref lck))
                {
                    var i = -1;
                    var h1 = handlers ?? new T?[(i = 0) | 1];
                    if (i == -1) i = Array.IndexOf(h1, null);
                    if (i == -1)
                    {
                        var h = new T?[(i = h1.Length) + 1];
                        Array.Copy(h1, h, i);
                        h1 = h;
                    }
                    h1[i] = handler;
                    handlers = h1;
                }
            }

            internal static void RemoveHandler<T>(ref object? lck, ref T?[]? handlers, T handler) where T : class //, Delegate
            {
                if (handler == default) return;
                lock (EnsureHandlersLock(ref lck))
                {
                    T?[]? h1 = null, h0 = handlers;
                    int i1 = 0, i0 = 0, len0 = (h0?.Length ?? 0);
                    for (; i0 < len0; i0++)
                    {
                        if (Equals(h0![i0], handler))
                        {
                            if (h1 != null) continue;
                            h1 = new T?[h0.Length];
                            if (i0 > 0) Array.Copy(h0, 0, h1, 0, i0 - 1);
                            i1 = i0;
                        }
                        else if (h1 != null)
                        {
                            h1[i1++] = h0[i0];
                        }
                    }
                    if (h1 != null)
                    {
                        switch (i1 = Array.IndexOf(h1, null))
                        {
                            case 0:
                                h1 = Array.Empty<T>();
                                break;
                            case > -1:
                                h0 = h1;
                                h1 = new T?[i1];
                                Array.Copy(h0, 0, h1, 0, i1);
                                break;
                        }
                        handlers = h1;
                    }
                }
            }

            static object EnsureHandlersLock(ref object? lck)
            {
                var o = lck;
                if (o != null) return o;
                o = new object();
                return Interlocked.CompareExchange(ref lck, o, null) ?? o;
            }

            public abstract Task? CallOnHandle(HuActor actor, ref IHuActorMessage message, in CancellationToken cancellation);
        }

        [DebuggerDisplay("_handlers = [{_handlers?.Length}]")]
        sealed class Handlers<T> : Handlers where T : HuActor
        {
            internal Func<T, IHuActorMessage, CancellationToken, Task?>?[]? _handlers;

            public override Task? CallOnHandle(HuActor actor, ref IHuActorMessage message, in CancellationToken cancellation)
            {
                var handlers = _handlers;
                if (handlers?.Length > 0)
                {
                    var actor1 = (T)actor;
                    foreach (var handler in handlers)
                    {
                        if (handler != null)
                        {
                            var t = handler.Invoke(actor1, message, cancellation);
                            if (t != null) return t;
                        }
                    }
                }
                return null;
            }
        }
    }
#nullable disable
}
