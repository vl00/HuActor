using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
    public class HuActorRTOptions
    {
        public int MaxIdleCount { get; set; } = 1;

        public TimeSpan TimeSpanForGCPeriod { get; set; } = TimeSpan.FromMinutes(1);

        public bool AutoResetIdleCount { get; set; } = false;

        // null is for `AppDomain.CurrentDomain.GetAssemblies().SelectMany(_ => _.GetTypes())`
        public IEnumerable<Type> InitializerTypes { get; set; } = null;  
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HuActorAttr : Attribute
    {
        public HuActorAttr(string name = null) => ActorName = name;

        public string ActorName { get; }
        public bool LckUseStrict { get; set; } = true;
        /// <summary>
        /// >0=true ; 0=false ; -1 is null by use default
        /// </summary>
        public int AutoResetIdleCount { get; set; } = -1;
        /// <summary>
        /// >=0 for count ; -1 for no gc ; -2 is null by use default
        /// </summary>
        public int MaxIdleCount { get; set; } = -2;
    }

    public sealed class HuActorTyInformation
    {
        public string ActorName { get; init; }
        public Type ActorType { get; init; }
        public bool LckUseStrict { get; init; }
        public bool? AutoResetIdleCount { get; init; }
        public int? MaxIdleCount { get; init; }

        public static HuActorTyInformation Get(Type actorType)
        {
            if (actorType == null || !typeof(HuActor).IsAssignableFrom(actorType)) return null;
            if (actorType == typeof(HuActor)) return new HuActorTyInformation { ActorType = actorType, LckUseStrict = false };

            var actorAttr = actorType.GetCustomAttribute<HuActorAttr>();

            return new HuActorTyInformation
            {
                ActorName = actorAttr?.ActorName ?? actorType.FullName,
                ActorType = actorType,
                LckUseStrict = actorAttr?.LckUseStrict ?? true,
                AutoResetIdleCount = actorAttr?.AutoResetIdleCount switch { null => null, 0 => false, > 0 => true, _ => null },
                MaxIdleCount = actorAttr?.MaxIdleCount switch { null => null, <= -2 => null, _ => actorAttr.MaxIdleCount },
            };
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HuActorRTInitializer : Attribute
    {
        public static void Initialize(IHuActorRT rt, IEnumerable<Type> types = null)
        {
            var tys = (types ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(_ => _.GetTypes()))
                .Where(_ => _.CustomAttributes.Any(a => a.AttributeType == typeof(HuActorRTInitializer)));

            foreach (var ty in tys)
            {
                var med = ty?.GetMethod(nameof(Initialize), BindingFlags.Public | BindingFlags.Static);
                _ = med?.GetParameters()?.Length switch
                {
                    0 => med.Invoke(null, null),
                    1 => med.Invoke(null, new object[] { rt }),
                    > 1 => throw new ArgumentOutOfRangeException($"args length of method='{nameof(Initialize)}' must not be over than 1."),
                    _ => null,
                };
            }
        }
    }

    public interface IHuActorFactory
    {
        HuActor CreateActor(in HuActorId actorId);
    }

    public interface IHuActorCtrlLock
    {
        [return: MaybeNull] 
        ValueTask<object> LockAsync(HuActorId actorId, CancellationToken cancellation = default);

        ValueTask UnLockAsync([AllowNull] object lck);
    }

    public partial class HuActorRT : IHuActorRT
    {
        readonly IDictionary<string, HuActorMgr> _aMgrs;

        internal readonly HuActorRTOptions _options;
        internal readonly Func<IHuActorFactory> _actorFactoryProvider;
        internal readonly IHuActorCtrlLock _actorCtrlLock;

        public HuActorRT(HuActorRTOptions options, Func<IHuActorFactory> actorFactoryProvider, IHuActorCtrlLock actorCtrlLock)
        {
            this._aMgrs = new Dictionary<string, HuActorMgr>();
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this._actorFactoryProvider = actorFactoryProvider ?? throw new ArgumentNullException(nameof(actorFactoryProvider));
            this._actorCtrlLock = actorCtrlLock;
        }

        public void RegisterActor<T>() where T : HuActor
        {
            var actorInfo = HuActorTyInformation.Get(typeof(T)) ?? throw new ArgumentNullException($"Actor's information not found, type='{typeof(T).FullName}'.");
            _aMgrs.Add(actorInfo.ActorName, new HuActorMgr { ActorTyInformation = actorInfo });
        }

        public Task UnloadAsync(HuActorId actorId, CancellationToken cancellation = default)
        {
#if DEBUG
            if (!cancellation.IsCancellationRequested) throw new NotSupportedException(); 
#endif
            return GetMgr(actorId.ActorName, false)?.UnloadActorAsync(this, actorId, cancellation) ?? Task.CompletedTask;
        }

        public Task HandleMessageAsync(HuActorId actorId, IHuActorMessage actorMessage, CancellationToken cancellation = default)
        {
            return GetMgr(actorId.ActorName, true)!.HandleActorMessageAsync(this, actorId, actorMessage, cancellation);
        }

        private HuActorMgr GetMgr(string actorName, bool throwOnError)
        {
            if (_aMgrs.TryGetValue(actorName, out var mgr)) return mgr;
            return throwOnError ? throw new InvalidOperationException($"Did not registered type for ActorName='{actorName}'.") : null;
        }

        public HuActorTyInformation GetActorTyInformation(string actorName, bool throwOnError = false)
            => GetMgr(actorName, throwOnError)?.ActorTyInformation;

        public HuActorTyInformation GetActorTyInformation(Type actorType)
            => _aMgrs.FirstOrDefault(_ => _.Value.ActorTyInformation.ActorType == actorType).Value?.ActorTyInformation ?? HuActorTyInformation.Get(actorType);
    }

    public partial class HuActorRT
    {
        Timer _gcTimer;
        CancellationTokenSource _cancelTS;

        public CancellationToken Stopping => _cancelTS?.Token ?? default;

        public static HuActorRT Instance;

        public Task StartGC()
        {
            if (_gcTimer == null)
            {
                _cancelTS = new();
                _gcTimer = new(static o =>
                {
                    var rt = (HuActorRT)o;
                    _ = rt!.OnGC(false, rt._cancelTS.Token).ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion);
                }, this, -1, -1);
                TryStartGCTimer();
            }
            return Task.CompletedTask;
        }

        public Task StopGC()
        {
            if (_gcTimer != null)
            {
                if (_cancelTS?.IsCancellationRequested == false) _cancelTS.Cancel();
                var _gct = _gcTimer;
                _gcTimer = null;
                _gct.Change(-1, -1);
                _gct.Dispose();
                return OnGC(true, default);
            }
            return Task.CompletedTask;
        }

        void TryStartGCTimer()
        {
            _gcTimer?.Change(_options.TimeSpanForGCPeriod, Timeout.InfiniteTimeSpan);
        }

        internal async Task OnGC(bool stopping, CancellationToken cancellation)
        {
            _gcTimer?.Change(-1, -1);
            List<Task> lsT = new();
            foreach (var mgr in _aMgrs.Values)
            {
                if (cancellation.IsCancellationRequested) return;
                mgr.OnGC(this, lsT, stopping, cancellation);
            }
            if (cancellation.IsCancellationRequested) return;
            if (lsT.Count > 0)
            {
                if (stopping) await Task.WhenAll(lsT);
                else _ = Task.WhenAll(lsT).ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion);
            }
            if (cancellation.IsCancellationRequested || stopping) return;
            TryStartGCTimer();
        }
    }

    internal class HuActorMgr
    {
        readonly ConcurrentDictionary<HuActorId, ActorWrapper> _actors = new();

        public HuActorTyInformation ActorTyInformation { get; init; }

        public Task UnloadActorAsync(HuActorRT rt, HuActorId actorId, CancellationToken cancellation = default)
        {
            if (ActorTyInformation.ActorName != actorId.ActorName) throw new InvalidOperationException();
            if (!_actors.TryGetValue(actorId, out var w) || !w.TryUse(null, ActorTyInformation)) return Task.CompletedTask;
            return InternalUnloadActorAsync(rt, rt._options, actorId, w, cancellation);
        }

        internal void OnGC(HuActorRT rt, List<Task> lsT, bool stopping, CancellationToken cancellation)
        {
            foreach (var (actorId, w) in _actors)
            {
                if (cancellation.IsCancellationRequested) return;
                if (stopping) w.MarkForCollectEarly();
                else if (!w.TryCollect(rt._options, ActorTyInformation)) continue;
                lsT.Add(InternalUnloadActorAsync(rt, null, actorId, w, cancellation));
            }
        }

        async Task InternalUnloadActorAsync(HuActorRT rt, HuActorRTOptions options, HuActorId actorId, ActorWrapper w, CancellationToken cancellation)
        {
            if (options == null) 
            {
                if (w.Actor == null) return;

                if (!ActorTyInformation.LckUseStrict)
                {
                    if (_actors.TryRemove(actorId, out w))
                        await w.CallOnUnload(cancellation);
                }
                else
                {
                    object alck = default;
                    try
                    {
                        alck = await rt._actorCtrlLock.LockAsync(actorId, cancellation);

                        if (w.Actor != null && _actors.TryRemove(new(actorId, w)))
                            await w.CallOnUnload(cancellation);
                    }
                    finally
                    {
                        await rt._actorCtrlLock.UnLockAsync(alck);
                    }
                }
            }
            //
            // for 'this.UnloadActorAsync' called
            else 
            {
                object alck = default;
                try
                {
                    if (w.Actor == null) return;

                    alck = await rt._actorCtrlLock.LockAsync(actorId, cancellation);

                    if (w.Actor != null && _actors.TryRemove(new(actorId, w)))
                        await w.CallOnUnload(cancellation);
                }
                finally
                {
                    await rt._actorCtrlLock.UnLockAsync(alck);
                    w.UnUse();
                }
            }
        }

        public async Task HandleActorMessageAsync(HuActorRT rt, HuActorId actorId, IHuActorMessage actorMessage, CancellationToken cancellation = default)
        {
            if (ActorTyInformation.ActorName != actorId.ActorName) throw new InvalidOperationException();
            ActorWrapper w;
            SpinWait sw = default;
            while (true)
            {
                if (!_actors.TryGetValue(actorId, out w)) 
                {
                    w = _actors.GetOrAdd(actorId, new ActorWrapper());
                }
                if (w.TryUse(rt._options, ActorTyInformation)) break;
                else sw.SpinOnce();
            }
            object alck = default;
            try
            {
                alck = await rt._actorCtrlLock.LockAsync(actorId, cancellation);

                try 
                {
                    var t = w.EnsureOnLoad(ref actorId, rt._actorFactoryProvider);
                    if (t != null) await t;
                }
                catch
                {
                    try { await w.CallOnUnload(cancellation); } catch { }
                    throw;
                }
             
                await w.CallOnHandle(actorId, actorMessage, cancellation);
            }
            finally
            {
                await rt._actorCtrlLock.UnLockAsync(alck);
                w.UnUse();
            }
        }
    }

    internal class ActorWrapper
    {
        IHuActorFactory _factory;
        int refCount, idleCount = -1;
        bool collected, collectEarly;

        internal HuActor Actor;

        internal Task EnsureOnLoad(ref HuActorId actorId, Func<IHuActorFactory> actorFactoryProvider)
        {
            if (Actor != null) return null;
            _factory ??= actorFactoryProvider.Invoke();
            Actor = _factory.CreateActor(actorId);
            return Actor.OnLoad();
        }

        internal async Task CallOnUnload(CancellationToken cancellation)
        {
            try
            {
                var a0 = Actor;
                if (a0 == null) return;
                Actor = null;
                await a0.OnUnload(); 
            }
            finally
            {                                                
                var fcy = _factory;
                if (fcy != null)
                {
                    _factory = null;                                               
                    (fcy as IDisposable)?.Dispose();
                }
            }
        }

        internal Task CallOnHandle(in HuActorId actorId, IHuActorMessage message, in CancellationToken cancellationToken)
        {            
            if (Actor == null) throw new HuActorIsDeletedException(actorId);
            return HuActorRT.InternalCallOnHandle(Actor, message, cancellationToken);
        }

        internal bool TryUse(HuActorRTOptions options, HuActorTyInformation actorTyInformation)
        {
            lock (this)
            {
                if (collected) return false;
                refCount++;
                if (options == null)
                {
                    MarkForCollectEarly();
                }
                else if (actorTyInformation?.AutoResetIdleCount ?? options!.AutoResetIdleCount || idleCount == -1) 
				{
					idleCount = 0; 
                }
                return true;
            }
        }

        internal void UnUse()
        {
            lock (this)
            {
                --refCount; 
            }
        }

        internal bool TryCollect(HuActorRTOptions options, HuActorTyInformation actorTyInformation)
        {
            lock (this)
            {
                if (collected || refCount > 0 || idleCount == -1)
                {
                    return false;
                }
                if (collectEarly || options == null) 
                {
                    return (collected = true);
                }
                var maxIdleCount = actorTyInformation?.MaxIdleCount ?? options!.MaxIdleCount;
                if (maxIdleCount != -1 && idleCount >= maxIdleCount)
                {
                    return (collected = true);
                }
                idleCount++;
                return false;
            }
        }

        internal void MarkForCollectEarly() => collectEarly = true;
    }

    public class HuActorIsDeletedException : Exception
    {
        public readonly HuActorId ActorId;

        public HuActorIsDeletedException(in HuActorId actorId, string message = null) : base(message ?? "Actor is deleted")
        {
            ActorId = actorId;
        }
    }

    public static class HuActorRTExtension
    {
        public static void UnloadOnIdle(this HuActorRT rt, HuActor actor)
        {
            if (rt == null || actor == null) return;
            _ = rt.UnloadAsync(actor.ActorId, new CancellationToken(true)) 
                .ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}