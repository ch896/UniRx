﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityRx
{
    // TODO:Subject? Scheduler?

    // Select, Where, SelectMany, Zip, Merge, CombineLatest, Switch, ObserveOn, Retry, Defer, Etc...

    // onNext implementation guide. enclose otherFunc but onNext is not catch.
    // try{ otherFunc(); } catch { onError() }
    // onNext();

    public interface IObservable<out T>
    {
        IDisposable Subscribe(IObserver<T> observer);
    }

    public static partial class Observable
    {
        static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, -1); // from .NET 4.5


        public static IObservable<TR> Select<T, TR>(this IObservable<T> source, Func<T, TR> selector)
        {
            return Observable.Create<TR>(observer =>
            {
                return source.Subscribe(Observer.Create<T>(x =>
                {
                    var v = selector(x);
                    observer.OnNext(v);
                }, observer.OnError, observer.OnCompleted));
            });
        }

        public static IObservable<T> Where<T>(this IObservable<T> source, Func<T, bool> predicate)
        {
            return Observable.Create<T>(observer =>
            {
                return source.Subscribe(Observer.Create<T>(x =>
                {
                    if (predicate(x))
                    {
                        observer.OnNext(x);
                    }
                }, observer.OnError, observer.OnCompleted));
            });
        }

        public static IObservable<TR> SelectMany<T, TR>(this IObservable<T> source, Func<T, IObservable<TR>> selector)
        {
            return source.Select(selector).Merge();
        }

        public static IObservable<TR> SelectMany<T, TC, TR>(this IObservable<T> source, Func<T, IObservable<TC>> collectionSelector, Func<T, TC, TR> selector)
        {
            return source.SelectMany(x => collectionSelector(x).Select(y => selector(x, y)));
        }

        public static IObservable<T> Merge<T>(this IObservable<IObservable<T>> sources)
        {
            return Observable.Create<T>(observer =>
            {
                var group = new CompositeDisposable();

                var first = sources.Subscribe(innerSource =>
                {
                    var d = innerSource.Subscribe(observer.OnNext);
                    group.Add(d);
                }, observer.OnError, observer.OnCompleted);

                group.Add(first);

                return group;
            });
        }

        public static IObservable<T> Delay<T>(this IObservable<T> source, TimeSpan dueTime)
        {
            return source.Delay(dueTime, Scheduler.GameLoop);
        }

        public static IObservable<T> Delay<T>(this IObservable<T> source, TimeSpan dueTime, IScheduler scheduler)
        {
            return Observable.Create<T>(observer =>
            {
                var group = new CompositeDisposable();

                var first = source.Subscribe(x =>
                {
                    var d = scheduler.Schedule(() => observer.OnNext(x), dueTime);
                    group.Add(d);
                }, observer.OnError, observer.OnCompleted);

                group.Add(first);

                return group;
            });
        }

        public static IObservable<T> ObserveOn<T>(this IObservable<T> source, IScheduler scheduler)
        {
            return Observable.Create<T>(observer =>
            {
                var group = new CompositeDisposable();

                var first = source.Subscribe(x =>
                {
                    var d = scheduler.Schedule(() => observer.OnNext(x));
                    group.Add(d);
                }, observer.OnError, observer.OnCompleted);

                group.Add(first);

                return group;
            });
        }

        public static IObservable<T[]> ToArray<T>(this IObservable<T> source)
        {
            return Observable.Create<T[]>(observer =>
            {
                var list = new List<T>();
                return source.Subscribe(x => list.Add(x), () =>
                {
                    try
                    {
                        observer.OnNext(list.ToArray());
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                });
            });
        }

        public static IObservable<TSource> Scan<TSource>(this IObservable<TSource> source, Func<TSource, TSource, TSource> func)
        {
            return Observable.Create<TSource>(observer =>
            {
                bool isFirst = true;
                TSource prev = default(TSource);
                return source.Subscribe(x =>
                {
                    if (isFirst)
                    {
                        isFirst = false;
                        prev = x;
                        observer.OnNext(x);
                    }
                    else
                    {
                        var next = func(prev, x);
                        prev = next;
                        observer.OnNext(next);
                    }
                }, observer.OnError, observer.OnCompleted);
            });
        }

        public static IObservable<TAccumulate> Scan<TSource, TAccumulate>(this IObservable<TSource> source, TAccumulate seed, Func<TAccumulate, TSource, TAccumulate> func)
        {
            return Observable.Create<TAccumulate>(observer =>
            {
                var prev = seed;
                observer.OnNext(seed);

                return source.Subscribe(x =>
                {
                    var next = func(prev, x);
                    prev = next;
                    observer.OnNext(next);
                }, observer.OnError, observer.OnCompleted);
            });
        }

        // TODO:needs comparer overload

        public static IObservable<T> DistinctUntilChanged<T>(this IObservable<T> source)
        {
            return source.DistinctUntilChanged(x => x);
        }

        public static IObservable<T> DistinctUntilChanged<T, TKey>(this IObservable<T> source, Func<T, TKey> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (selector == null) throw new ArgumentNullException("selector");

            return Observable.Create<T>(observer =>
            {
                var isFirst = true;
                var prevKey = default(TKey);
                return source.Subscribe(x =>
                {
                    TKey currentKey;
                    try
                    {
                        currentKey = selector(x);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                        return;
                    }

                    var sameKey = false;
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        try
                        {
                            sameKey = currentKey.Equals(prevKey);
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                            return;
                        }
                    }
                    if (!sameKey)
                    {
                        prevKey = currentKey;
                        observer.OnNext(x);
                    }
                }, observer.OnError, observer.OnCompleted);
            });
        }


        public static T Wait<T>(this IObservable<T> source)
        {
            return source.WaitCore(throwOnEmpty: true, timeout: InfiniteTimeSpan);
        }

        public static T Wait<T>(this IObservable<T> source, TimeSpan timeout)
        {
            return source.WaitCore(throwOnEmpty: true, timeout: timeout);
        }

        static T WaitCore<T>(this IObservable<T> source, bool throwOnEmpty, TimeSpan timeout)
        {
            var semaphore = new System.Threading.ManualResetEvent(false);

            var seenValue = false;
            var value = default(T);
            var ex = default(Exception);

            using (source.Subscribe(
                onNext: x => { seenValue = true; value = x; },
                onError: x => { ex = x; semaphore.Set(); },
                onCompleted: () => semaphore.Set()))
            {
                var waitComplete = (timeout == InfiniteTimeSpan)
                    ? semaphore.WaitOne()
                    : semaphore.WaitOne(timeout);

                if (!waitComplete)
                {
                    throw new TimeoutException("OnCompleted not fired.");
                }
            }

            if (ex != null) throw ex;
            if (throwOnEmpty && !seenValue) throw new InvalidOperationException("No Elements.");

            return value;
        }
    }
}