using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace HuLyega
{
    /// <summary>
    /// an unsafe async lock that didn't await a same valuetask too many time 
    /// <br/>
    /// <br/> using (await HuLyegaLock.LockAsync("kkkk"))  
    /// <br/> {                                          
    /// <br/>     // your codes                          
    /// <br/> }                                          
    /// </summary>
    public class HuLyegaLock
    {
        private static readonly Dictionary<string, AsyncLockObj> LockObjs = new();
        private static readonly Stack<AsyncLockObj> Pool = new(MaxPoolSize);
        private const int MaxPoolSize = 16;
        private static SpinLock _spinLock = new(false);

        private HuLyegaLock() { }

        public static HuLyegaLock OnlyForDebugger => null;

        public static ValueTask<IDisposable> LockAsync(CancellationToken cancellation = default) => GetLockObj(null).LockAsync(cancellation);
        public static ValueTask<IDisposable> LockAsync(string k, CancellationToken cancellation = default) => GetLockObj(k).LockAsync(cancellation);

        #region RunOnLockAsync

        public static ValueTask RunOnLockAsync(Func<object, CancellationToken, Task> func, object o, CancellationToken cancellation = default) => RunOnLockAsync(null, func, o, cancellation);
        public static ValueTask RunOnLockAsync(Func<CancellationToken, Task> func, CancellationToken cancellation = default) => RunOnLockAsync(null, func, cancellation);
        public static ValueTask<T> RunOnLockAsync<T>(Func<object, CancellationToken, Task<T>> func, object o, CancellationToken cancellation = default) => RunOnLockAsync(null, func, o, cancellation);
        public static ValueTask<T> RunOnLockAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellation = default) => RunOnLockAsync(null, func, cancellation);

        public static async ValueTask RunOnLockAsync(string k, Func<object, CancellationToken, Task> func, object o, CancellationToken cancellation = default)
        {
            using (await LockAsync(k, cancellation))
            {
                await func(o, cancellation);
            }
        }

        public static async ValueTask RunOnLockAsync(string k, Func<CancellationToken, Task> func, CancellationToken cancellation = default)
        {
            using (await LockAsync(k, cancellation))
            {
                await func(cancellation);
            }
        }

        public static async ValueTask<T> RunOnLockAsync<T>(string k, Func<object, CancellationToken, Task<T>> func, object o, CancellationToken cancellation = default)
        {
            using (await LockAsync(k, cancellation))
            {
                return await func(o, cancellation);
            }
        }

        public static async ValueTask<T> RunOnLockAsync<T>(string k, Func<CancellationToken, Task<T>> func, CancellationToken cancellation = default)
        {
            using (await LockAsync(k, cancellation))
            {
                return await func(cancellation);
            }
        }

        #endregion RunOnLockAsync        

        private static AsyncLockObj GetLockObj(string key)
        {
            AsyncLockObj lck;
            key ??= string.Empty;
            var lockTaken = false;            
            try
            {
                _spinLock.Enter(ref lockTaken);

                if (!LockObjs.TryGetValue(key, out lck))
                {
                    lck = Pool.TryPop(out var x) ? x : new();
                    lck.Key = key;
                    LockObjs.Add(key, lck);
                }

                lck.RefCount++;
            }
            finally
            {
                if (lockTaken)                
                    _spinLock.Exit();                
            }
            return lck;
        }

        private static void ReleaseLockObj(AsyncLockObj lck)
        {
            var lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);

                if (--lck.RefCount == 0 && lck.Key != null)
                {
                    LockObjs.Remove(lck.Key);
                    if (Pool.Count < MaxPoolSize)
                    {
                        lck.Key = null;
                        Pool.Push(lck);
                    }
                }
            }
            finally
            {
                if (lockTaken)                
                    _spinLock.Exit();                
            }
        }

        sealed class AsyncLockObj : IDisposable, IValueTaskSource<IDisposable>
        {
            readonly Queue<Item> _waitings = new(8);
            int _status;

            internal string Key;
            internal int RefCount;

            public ValueTask<IDisposable> LockAsync(CancellationToken cancellation = default)
            {
                return new(!cancellation.CanBeCanceled ? this : new AsyncLockObjCT(this, cancellation), default);
            }

            ValueTaskSourceStatus IValueTaskSource<IDisposable>.GetStatus(short token) => ValueTaskSourceStatus.Pending;

            void IValueTaskSource<IDisposable>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags) => OnCompleted(continuation, state, ref token, ref flags);

            private void OnCompleted(Action<object> continuation, object state, ref short token, ref ValueTaskSourceOnCompletedFlags flags)
            {
                Action<object> c = null;
                lock (_waitings)
                {
                    var status = _status;
                    if (status == 0)
                    {
                        _status = 1;
                        c = continuation;
                    }
                    else _waitings.Enqueue(new(continuation, state));
                }
                c?.Invoke(state);
            }

            IDisposable IValueTaskSource<IDisposable>.GetResult(short token)
            {
                if (_status != 1) throw new Exception("please use 'await'");
                return this;
            }

            public void Dispose()
            {
                Item n;
                lock (_waitings)
                {
                    if (!_waitings.TryDequeue(out n))
                        Volatile.Write(ref _status, 0);
                }

                ReleaseLockObj(this);

                if (n.Continuation != null) 
                {
                    ThreadPool.UnsafeQueueUserWorkItem(n.Continuation, n.State, false);
                }
            }

            [StructLayout(LayoutKind.Auto)]
            readonly struct Item
            {
                public readonly Action<object> Continuation;
                public readonly object State;

                public Item(Action<object> continuation, object state)
                {
                    Continuation = continuation;
                    State = state;
                }
            }

            sealed class AsyncLockObjCT : IValueTaskSource<IDisposable>
            {
                readonly AsyncLockObj origin;                
                CancellationTokenRegistration creg;
                Action<object> _continuation;
                object _continuationState;
                ExceptionDispatchInfo _error;
                int _st;

                public AsyncLockObjCT(AsyncLockObj origin, in CancellationToken cancellation)
                {
                    this.origin = origin;

                    creg = cancellation.UnsafeRegister(static (o, ctk) => ((AsyncLockObjCT)o)!.CompleteOnCancel(ctk), this);
                }

                static readonly Action<object> s_completedAction = (o) => Debug.Fail("Completed end. Usually cancel was called very fast");

                static readonly Action<object> s_holdCallbackAction = (o) => ((AsyncLockObjCT)o)!.CompleteOnHoldOK();

                ValueTaskSourceStatus IValueTaskSource<IDisposable>.GetStatus(short token)
                {
                    return _error != null ? ValueTaskSourceStatus.Canceled
                        : ValueTaskSourceStatus.Pending;
                }

                void IValueTaskSource<IDisposable>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
                {
                    if (_continuationState != null) throw new InvalidOperationException("Don't await too many time");
                    _continuationState = state;

                    var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
                    if (prevContinuation != null)
                    {
                        if (!ReferenceEquals(prevContinuation, s_completedAction))
                            throw new InvalidOperationException("Don't await too many time");

                        continuation(state);
                        return;
                    }

                    origin.OnCompleted(s_holdCallbackAction, this, ref token, ref flags);
                }

                IDisposable IValueTaskSource<IDisposable>.GetResult(short token)
                {
                    try
                    {
                        _error?.Throw();
                        return origin;
                    }
                    finally
                    {
                        creg = default;
                        //_continuation = s_completedAction;
                        _continuationState = Task.CompletedTask;
                    }
                }

                private void CompleteOnCancel(in CancellationToken cancellation)
                {
                    if (_st != 0 || Interlocked.CompareExchange(ref _st, -1, 0) != 0) return;

                    _error = ExceptionDispatchInfo.Capture(new OperationCanceledException(cancellation));

                    DoCallback_await(_continuation);
                }                

                private void CompleteOnHoldOK()
                {
                    creg.Dispose();

                    if (_st != 0 || Interlocked.CompareExchange(ref _st, 1, 0) != 0)
                    {
                        origin.Dispose();
                        return;
                    }
                    
                    DoCallback_await(_continuation);
                }

                void DoCallback_await(Action<object> c)
                {
                    if (c != null || (c = Interlocked.CompareExchange(ref _continuation, s_completedAction, null)) != null)
                    {
                        _continuation = s_completedAction;
                        c(_continuationState);
                    }
                }
            }
        }
    }
}
