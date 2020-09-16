![](docs/img/Banner.gif)

> All project updates are published on our [Discord Server]; it's also the best place for Q/A.\
> [![Build](https://github.com/servicetitan/Stl.Fusion/workflows/Build/badge.svg)](https://github.com/servicetitan/Stl.Fusion/actions?query=workflow%3A%22Build%22)
> [![NuGetVersion](https://img.shields.io/nuget/v/Stl.Fusion)](https://www.nuget.org/packages?q=Owner%3Aservicetitan+Tags%3Astl_fusion) 

## What is Stl.Fusion?

`Stl.Fusion` is a [.NET Core](https://en.wikipedia.org/wiki/.NET_Core) library
providing a new change tracking abstraction built in assumption that **every piece of data 
you have is a part of the observable state / model**, and since there is 
no way to fit such a huge state in RAM, Fusion:
* Spawns the **observed part** of this state on-demand
* **Holds the dependency graph of any observed state in memory** to make sure 
  every dependency of this state triggers cascading invalidation once it gets 
  changed.
* And finally, **it does all of this automatically and transparently for you**, 
  so Fusion-based code is [almost identical](#enough-talk-show-me-the-code)
  to the code you'd write without it.

This is quite similar to what any 
[MMORPG](https://en.wikipedia.org/wiki/Massively_multiplayer_online_role-playing_game) 
game engine does: even though the complete game state is huge, it's still possible to 
run the game in real time for 1M+ players, because every player observes 
a tiny fraction of a complete game state, and thus all you need is to ensure
this part of the state fits in RAM + you have enough computing power to process
state changes for every player.

### Build a Real-Time UI

This is [Fusion Blazor Sample](https://github.com/servicetitan/Stl.Fusion.Samples),
delivering real-time updates to 3 browser windows:

![](docs/img/Stl-Fusion-Chat-Sample.gif)

The sample supports **both (!)** Server-Side Blazor and Blazor WebAssembly 
[hosting modes](https://docs.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-3.1)
&ndash; you can switch the mode on its "Home" page.

![](docs/img/Samples-Blazor-DualMode.gif)

### Speedup Your Service By Caching Everything

[A small benchmark in Stl.Fusion test suite](https://github.com/servicetitan/Stl.Fusion/blob/master/tests/Stl.Fusion.Tests/PerformanceTest.cs) 
compares "raw" [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/) - 
based Data Access Layer (DAL) against its version relying on Fusion. 
Both tests run *almost* identical code - in fact, the only difference is that Fusion
version of this test uses Fusion-provided proxy wrapping the 
[`UserService`](https://github.com/servicetitan/Stl.Fusion/blob/master/tests/Stl.Fusion.Tests/Services/UserService.cs)
(the DAL used in this test) instead of the actual type.

The performance difference looks shocking at first:

![](docs/img/Performance.gif)

The speedup is:
* ~31,500x for [Sqlite EF Core Provider](https://www.sqlite.org/index.html) 
* ~1,000x for [In-memory EF Core Provider](https://docs.microsoft.com/en-us/ef/core/providers/in-memory/?tabs=dotnet-core-cli)  

Since Fusion precisely knows when every result - even the intermediate one - 
gets inconsistent with the ground truth, it also ensures that 
**every result is computed just once and reused until it gets invalidated**.
In other words, **Fusion also provides a transparent cache**, and that's
why you see such a speedup.

You can control how such caching works, and even though it's an in-process cache
(which is why it speeds up even in-memory EF Core provider by 1000x),
Fusion supports "swapping" to any external cache (e.g. Redis) as well.

Note that:
* Similarly to real-time updates, *you get this speedup for free* in terms of extra code.
* You also get *almost always consistent* cache. 
  It's still an *eventually consistent* cache, of course, but the inconsistency periods 
  for cache entries are so short that normally don't need to worry about the inconsistencies.
* The speedup you're expected to see in production may differ from these numbers a lot. 
  Even though the results presented here are absolutely real, they are produced on a synthetic test.

## How Stl.Fusion works?

Fusion provides three key abstractions:
* [Compute Services] are services exposing methods "backed" by Fusion's 
  version of "computed observables". Compute Services are responsible for 
  "spawning" parts of the state on-demand.
* [Replica Services] - remote proxies of Compute Services.
  They allow remote clients to consume ("observe") the parts of server-side state.
  They typically "substitute" similar [Compute Services] on the client side.
* And finally, [`IComputed<T>`] &ndash; an observable [Computed Value]
  that's in some ways similar to the one you can find in Knockout, MobX, or Vue.js,
  but very different, if you look at its fundamental properties.
    
[`IComputed<T>`] is:
* **Thread-safe**
* **Asynchronous** &ndash; any [Computed Value] is computed asynchronously; 
  Fusion APIs dependent on this feature are also asynchronous.
* **Almost immutable** &ndash; once created, the only change that may happen to it is transition 
  to `IsConsistent() == false` state
* **GC-friendly** &ndash; if you know about 
  [Pure Computed Observables](https://knockoutjs.com/documentation/computed-pure.html) 
  from Knockout, you understand the problem. [`IComputed<T>`] solves it even better &ndash;
  dependent-dependency relationships are explicit there, and the reference pointing
  from dependency to dependent is [weak](https://en.wikipedia.org/wiki/Weak_reference), 
  so any dependent [Computed Value] is available for GC unless it's referenced by something 
  else (i.e. used).

All above make it possible to use [`IComputed<T>`] on the server side &ndash; 
you don't have to synchronize access to it, you can use it everywhere, including
async functions, and you don't need to worry about GC.

But there is more &ndash; any [Computed Value]:

* **Is computed just once** &ndash; when you request the same Computed Value at the same time 
  from multiple (async) threads and it's not cached yet, just one of these threads will
  actually run the computation.  Every other async thread will await till its completion 
  and return the newly cached instance.
* **Updated on demand** &ndash; once you have an [`IComputed<T>`], you can ask for its
  consistent version at any time. If the current version is consistent, you'll get the 
  same object, otherwise you'll get a *newly computed* consistent version, 
  and every other version of it  is guaranteed to be marked inconsistent.
  At glance, it doesn't look like a useful property, but together with immutability and
  "computed just once" model, it de-couples invalidations (change notifications) 
  from updates, so ultimately, you are free to decide for how long to delay the 
  update once you know certain state is inconsistent.
* **Supports remote replicas** &ndash; any Computed Value instance can be *published*, 
  which allows any other code that knows the publication endpoint and publication ID 
  to create a replica of this [`IComputed<T>`] instance in their own process. 
  [Replica Services] mentioned above rely on this feature.

### Why these features are game changing?

> The ability to replicate any server-side state to any client allows client-side code 
  to build a dependent state that changes whenever any of its server-side components
  change. 
  This client-side state can be, for example, your UI model, that instantly reacts
  to the changes made not only locally, but also remotely!

> De-coupling updates from invalidation events enables such apps to scale. 
  You absolutely need the ability to control the update delay, otherwise 
  your app is expected to suffer from `O(N^2)` update rate on any 
  piece of popular content (that's both viewed and updated by a large number of users).

The last issue is well-described in 
["Why not LiveQueries?" part in "Subscriptions in GraphQL"](https://graphql.org/blog/subscriptions-in-graphql-and-relay/), 
and you may view `Stl.Fusion` as 95% automated solution for this problem:
* **It makes recomputations cheap** by caching all the intermediates
* It de-couples updates from the invalidations to ensure 
  **any subscription has a fixed / negligible cost**.
  
If you have a post viewed by 1M users and updated with 1 KHz frequency 
(usually the frequency is proportional to the count of viewers too), 
it's 1B of update messages per second to send for your servers
assuming you try to deliver every update to every user. 
**This can't scale.** 
But if you switch to 10-second update delay, your update frequency 
drops by 10,000x to just 100K updates per second. 
Note that 10 second delay for seeing other people's updates is 
something you probably won't even notice.

`Stl.Fusion` allows you to control such delays precisely.
You may use a longer delay (10 seconds?) for components rendering
"Likes" counters, but almost instantly update comments. 
The delays can be dynamic too &ndash; the simplest example of 
behavior is instant update for any content you see that was invalidated 
right after your own action.

## Enough talk. Show me the code!

[Compute Services] is where a majority of Fusion-based code is supposed to live.
[CounterService](https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/src/HelloBlazorServer/Services/CounterService.cs)
from [HelloBlazorServer sample](https://github.com/servicetitan/Stl.Fusion.Samples)
is a good example of such a service:

![](docs/img/CounterService.gif)

Lime-colored parts show additions to a similar singleton service
you'd probably have in case when real-time updates aren't needed:
* `[ComputeMethod]` indicates that any `GetCounterAsync` result should be 
  "backed" by [Computed Value]. 
  This attribute works only when you register a service as [Compute Service] 
  in IoC container and the method it is applied to is virtual.
* `Computed.Invalidate` call finds a [Computed Value] "backing" the most recent
  `GetCounterAsync` call with the same arguments (no arguments in this case) 
  and invalidates it - unless it doesn't exist or was invalidated earlier.
  We have to manually invalidate this value because `GetCounterAsync`
  doesn't call any other [Compute Services], and thus its result doesn't
  have any dependencies which otherwise would auto-invalidate it.

[Counter.razor](https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/src/HelloBlazorServer/Pages/Counter.razor) is a Blazor Component that uses
`CounterService`:

![](docs/img/CounterRazor.gif)

Again, lime-colored parts show additions to a similar Blazor Component without 
real-time updates:
* It inherits from [LiveComponentBase<T>](https://github.com/servicetitan/Stl.Fusion/blob/master/src/Stl.Fusion.Blazor/LiveComponentBase.cs) - a small wrapper over
  `ComponentBase` (base class for any Blazor component), which adds
  `State` property and abstract `ComputeStateAsync` method allowing to
  (re)compute the `State.Value` once any of its dependencies changes.
* `LiveComponent<T>.State` property is a [Live State] - an object exposing 
  the most current [Computed Value] produced by a computation (`Func<...>`)
  and making sure it gets recomputed with a controllable delay 
  after any of its dependencies change.
* As you might guess, `ComputeStateAsync` defines `State.Value` computation logic
  in any `LiveComponentBase<T>` descendant.

Blue-colored parts show how `State` is used:
* `State.LastValue` is the most recent non-error value produced by the computation.
  It's a "safe pair" to `State.Value` (true most recent computation result), 
  which throws an error if `State.Error != null`.
* `State.Error` contains an exception thrown by `ComputeStateAsync` when it fails,
  otherwise `null`.

That's *almost literally* (minus IoC registration) all you need to have this:
![](docs/img/Stl-Fusion-HelloBlazorServer-Counter-Sample.gif)

And if you're curious how "X seconds ago" gets updated,
notice that `ComputeStateAsync` invokes `TimeService.GetMomentsAgoAsync`,
which looks as follows:

![](docs/img/TimeService.gif)

In other words, `ComputeStateAsync` becomes dependent on "moments ago"
value, and this value self-invalidates ~ at the right moment triggering 
cascading `ComputeStateAsync` invalidation.

"Simple Chat" is a bit more complex example showing another interesting
aspect of this approach:
> Since *any event* describes *a change*, Fusion's only "invalidated" event 
> ("the output of f(...) changed") allows you to implement a reaction to 
> *nearly any* change without a need for a special event!

"Simple Chat" features a chat bot that listens to new chat messages and
responds to them:

![](docs/img/Stl-Fusion-HelloBlazorServer-SimpleChat-Sample.gif)

`ChatService` source code doesn't have any special logic to support chat bots - 
similarly to `CounterService`, it's almost the same as a similar service that
doesn't support any kind of real-time behavior at all:

![](docs/img/ChatService.gif)

But since `ChatService` is a [Compute Service], you can implement a "listener" 
reacting to changes in `GetMessagesAsync` output relying on e.g. [Live State] - 
and 
[that's exactly what `ChatBotService` does](https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/src/HelloBlazorServer/Services/ChatBotService.cs).

## Next Steps

* Check out [Tutorial], [Samples], or go to [Documentation Home]
* Join our [Discord Server] to ask questions and track project updates.

## Posts And Other Content
* [Why real-time UI is inevitable future for any web app?](https://medium.com/@alexyakunin/features-of-the-future-web-apps-part-1-e32cf4e4e4f4?source=friends_link&sk=65dacdbf670ef9b5d961c4c666e223e2)
* [How similar is Stl.Fusion to SignalR?](https://medium.com/@alexyakunin/how-similar-is-stl-fusion-to-signalr-e751c14b70c3?source=friends_link&sk=241d5293494e352f3db338d93c352249)
* [How similar is Stl.Fusion to Knockout / MobX?](https://medium.com/@alexyakunin/how-similar-is-stl-fusion-to-knockout-mobx-fcebd0bef5d5?source=friends_link&sk=a808f7c46c4d5613605f8ada732e790e)
* [Stl.Fusion In Simple Terms](https://medium.com/@alexyakunin/stl-fusion-in-simple-terms-65b1975967ab?source=friends_link&sk=04e73e75a52768cf7c3330744a9b1e38)


**P.S.** If you've already spent some time learning about Stl.Fusion, 
please help us to make it better by completing [Stl.Fusion Feedback Form] 
(1&hellip;3 min).

[Compute Services]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part01.md
[Compute Service]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part01.md
[`IComputed<T>`]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part02.md
[Computed Value]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part02.md
[Live State]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part03.md
[Replica Services]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/Part04.md
[Overview]: docs/Overview.md
[Documentation Home]: docs/README.md
[Samples]: https://github.com/servicetitan/Stl.Fusion.Samples
[Tutorial]: https://github.com/servicetitan/Stl.Fusion.Samples/blob/master/docs/tutorial/README.md

[Discord Server]: https://discord.gg/EKEwv6d
[Stl.Fusion Feedback Form]: https://forms.gle/TpGkmTZttukhDMRB6
