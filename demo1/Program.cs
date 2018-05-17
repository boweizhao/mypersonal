namespace demo1
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Concurrent;

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("0: {0}", Event.SeedCheckedIn.ToString());
            Console.WriteLine("1: {0}", Event.ValidationFailed.ToString());
            Console.WriteLine("2: {0}", Event.ValidationSucceeded.ToString());
            Console.WriteLine("3: {0}", Event.GraphCheckedIn.ToString());

            var test = new MonkeyServiceFsm();
            var task = test.StartAsync();

            do
            {
                var keyInfo = Console.ReadKey(true);
                if ((keyInfo.Modifiers & ConsoleModifiers.Alt) != 0 ||
                    (keyInfo.Modifiers & ConsoleModifiers.Control) != 0 ||
                    (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0)
                {
                    continue;
                }

                if (keyInfo.Modifiers == 0 && keyInfo.Key == ConsoleKey.Escape)
                {
                    break;
                }

                if (keyInfo.Modifiers == 0 && keyInfo.Key == ConsoleKey.D0)
                {
                    test.PutEvent(Event.SeedCheckedIn);
                }
                else if (keyInfo.Modifiers == 0 && keyInfo.Key == ConsoleKey.D1)
                {
                    test.PutEvent(Event.ValidationFailed);
                }
                else if (keyInfo.Modifiers == 0 && keyInfo.Key == ConsoleKey.D2)
                {
                    test.PutEvent(Event.ValidationSucceeded);
                }
                else if (keyInfo.Modifiers == 0 && keyInfo.Key == ConsoleKey.D3)
                {
                    test.PutEvent(Event.GraphCheckedIn);
                }
            } while (true);
        }

        internal enum Event : int
        {
            SeedCheckedIn = 0,
            ValidationFailed,
            ValidationSucceeded,
            GraphCheckedIn
        }

        internal enum State
        {
            WaitingForSeed,
            GraphGenerated,
            PrSubmitted
        }

        internal struct StateMap<StateT, EventT>
            where StateT : struct, System.Enum
            where EventT : struct, System.Enum
        {
            public StateT From { get; set; }
            public EventT Trigger { get; set; }
            public StateT To { get; set; }
            public Func<Task> TrasitionCallbackAsync { get; set; }
        }

        internal abstract class FsmBase<StateT, EventT>
            where StateT : struct, System.Enum
            where EventT : struct, System.Enum
        {
            private Dictionary<Tuple<StateT, EventT>, Tuple<StateT, Func<Task>>> stateTransitionMap;
            private ConcurrentQueue<EventT> eventQueue;
            private Queue<TaskCompletionSource<bool>> eventWaitingQueue;
            private object waitingQueueLock;
            private TaskFactory taskFactory;

            protected FsmBase(StateT initialState)
            {
                stateTransitionMap = new Dictionary<Tuple<StateT, EventT>, Tuple<StateT, Func<Task>>>();
                eventQueue = new ConcurrentQueue<EventT>();
                eventWaitingQueue = new Queue<TaskCompletionSource<bool>>();
                waitingQueueLock = new object();
                CurrentState = initialState;
                CurrentEvent = null;
                var schedulerPair = new ConcurrentExclusiveSchedulerPair();
                taskFactory = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, schedulerPair.ConcurrentScheduler);
            }

            public StateT CurrentState { get; private set; }

            public EventT? CurrentEvent { get; private set; }

            protected void RegisterStateTransitionCallback(params StateMap<StateT, EventT>[] stateMapItems)
            {
                foreach (var map in stateMapItems)
                {
                    stateTransitionMap[Tuple.Create(map.From, map.Trigger)] = Tuple.Create(map.To, map.TrasitionCallbackAsync);
                }
            }

            public async Task StartAsync()
            {
                var tasks = new Task[2];
                tasks[0] = taskFactory.StartNew(WaitingEventTask).Unwrap();
                tasks[1] = taskFactory.StartNew(
                    async () =>
                    {
                        while (true)
                        {
                            await WaitEventQueue();
                            await Task.Yield();
                            if (CurrentEvent.HasValue)
                            {
                                Tuple<StateT, Func<Task>> callback;
                                if (stateTransitionMap.TryGetValue(Tuple.Create(CurrentState, CurrentEvent.Value), out callback))
                                {
                                    await callback.Item2();
                                    CurrentState = callback.Item1;
                                }
                                else
                                {
                                    Console.WriteLine("Event: {0} in State: {1} is not supported.", CurrentEvent, CurrentState);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Event: {0} in State: {1} is not supported.", CurrentEvent, CurrentState);
                            }
                        }
                    }).Unwrap();

                await Task.WhenAll(tasks);
            }

            public void PutEvent(EventT @event)
            {
                eventQueue.Enqueue(@event);
                lock (waitingQueueLock)
                {
                    TaskCompletionSource<bool> tcs;
                    while (eventWaitingQueue.TryDequeue(out tcs))
                    {
                        tcs.TrySetResult(true);
                    }
                }
            }

            private async Task WaitingEventTask()
            {
                while (true)
                {
                    EventT @event;
                    if (eventQueue.TryDequeue(out @event))
                    {
                        CurrentEvent = @event;
                        Console.WriteLine("Event: {0}.", CurrentEvent);
                    }
                    else
                    {
                        CurrentEvent = null;
                    }

                    await Task.Yield();
                    await WaitEventQueue();
                }
            }

            private async Task WaitEventQueue()
            {
                Task waitTask;
                lock (waitingQueueLock)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    eventWaitingQueue.Enqueue(tcs);
                    waitTask = tcs.Task;
                }

                await waitTask;
            }
        }

        internal class MonkeyServiceFsm : FsmBase<State, Event>
        {
            public MonkeyServiceFsm()
                : base(State.WaitingForSeed)
            {
                RegisterStateTransitionCallback(
                    new StateMap<State, Event>
                    {
                        From = State.WaitingForSeed,
                        To = State.GraphGenerated,
                        Trigger = Event.SeedCheckedIn,
                        TrasitionCallbackAsync = MoveToGraphGenerated
                    },
                    new StateMap<State, Event>
                    {
                        From = State.GraphGenerated,
                        To = State.PrSubmitted,
                        Trigger = Event.ValidationSucceeded,
                        TrasitionCallbackAsync = MoveToPrSubmitted
                    },
                    new StateMap<State, Event>
                    {
                        From = State.PrSubmitted,
                        To = State.WaitingForSeed,
                        Trigger = Event.GraphCheckedIn,
                        TrasitionCallbackAsync = MoveToWaitingForSeedFromPrSubmitted
                    },
                    new StateMap<State, Event>
                    {
                        From = State.GraphGenerated,
                        To = State.WaitingForSeed,
                        Trigger = Event.ValidationFailed,
                        TrasitionCallbackAsync = MoveToWaitingForSeedFromGraphGenerated
                    });
            }

            private Task MoveToGraphGenerated()
            {
                Console.WriteLine("From {0} to {1}, seed check in detected, generating the graph.", State.WaitingForSeed, State.GraphGenerated);
                Console.WriteLine("The graph generated, waiting the validation results.");
                return Task.FromResult(0);
            }

            private Task MoveToPrSubmitted()
            {
                Console.WriteLine("From {0} to {1}, validation succeeded, submitting the PR.", State.GraphGenerated, State.PrSubmitted);
                Console.WriteLine("The PR was submitted, waiting the PR completion.");
                return Task.FromResult(0);
            }

            private Task MoveToWaitingForSeedFromPrSubmitted()
            {
                Console.WriteLine("From {0} to {1}, PR completed, waiting the seed.", State.PrSubmitted, State.WaitingForSeed);
                Console.WriteLine("The graph checked in, waiting the new seed.");
                return Task.FromResult(0);
            }

            private Task MoveToWaitingForSeedFromGraphGenerated()
            {
                Console.WriteLine("From {0} to {1}, the validation failed, waiting the seed.", State.GraphGenerated, State.WaitingForSeed);
                Console.WriteLine("The validation failed, waiting the new seed.");
                return Task.FromResult(0);
            }
        }
    }
}