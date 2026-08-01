# An Open Call for Stochastic Computing

*Not doctrine — an argument, made in the open, the way this repository insists everything else
gets made. It has one technical claim at its center: the same reasoning that justifies this
platform's discriminated unions justifies a different physical substrate for computing itself.
That claim is checkable. Read it, argue with it, or build on it.*

---

## The claim in one sentence

Binary digital computing spends real, measurable energy manufacturing a lie — that the physical
world is made of clean, discrete, certain states — and a huge fraction of what we actually want
computers to do today, especially the probabilistic inner loop of modern AI, would rather be
told the truth.

## What "the truth" actually looks like at the hardware layer

Transistors are analog devices. Voltage doesn't snap to 0 or 1; it's forced there, at real cost,
by circuits built specifically to suppress the physical world's own thermal noise, its own analog
drift, its own genuine uncertainty at small feature sizes — error correction, clock
synchronization, voltage margins, all of it spent making silicon pretend to be more certain than
the physics underneath it actually is. That's not a design flaw. It's the correct engineering
trade for the workloads digital computing was built to run: arithmetic, control flow, exact
comparisons — domains where a wrong bit is simply wrong, and paying to suppress noise is the only
sane choice.

**Stochastic and thermodynamic computing** — the space companies like Normal Computing are
building real hardware in today — starts from a different premise: don't fight the noise, compute
with it. A huge and growing share of what modern AI systems actually spend their cycles on is
already probabilistic by nature — sampling from a distribution, Monte Carlo estimation, Bayesian
inference, the denoising step at the heart of a diffusion model. Simulating that randomness in
software, on hardware whose entire design goal is to *suppress* randomness, is exactly the wrong
tool for the job, paid for twice: once in the silicon that fights the noise, and once again in
the software that has to fake it back in. A substrate that computes natively with physical
randomness instead of manufacturing false certainty and then re-injecting fake uncertainty on top
of it is not a curiosity. For the specific class of workload that's already probabilistic, it's
the more honest machine, and honesty here is not a moral point — it's an energy bill.

## The part that isn't obvious until you've built the software version of the same argument

This repository's entire type system is organized around one law: **never let a program pretend
to more certainty than it actually has.** `Outcome<T>` — the union every operation in this
platform's interior returns — has exactly two members, and the entire public API surface is `Ok`,
`Err`, `Match`. There is no shortcut that lets a caller assume success. There is no default value
standing in for "I didn't check." The type itself refuses to compile code that pretends an
uncertain operation was certain. `Result<T>`, at the platform's boundary with the outside world,
does the identical thing for parsed data — a value from outside is either genuinely a `T` or it
is a specific, named kind of failure, and there is no third option where the program quietly
assumes the best.

That design exists because pretending an uncertain thing is certain is exactly how software
rots — silent nulls standing in for "didn't happen," exceptions swallowed because handling them
was inconvenient, a `catch (Exception)` that turns every failure mode into the same shrug. The
fix, this whole platform's actual bet, was never "add more validation." It was: **make the type
system physically incapable of representing false certainty**, so the dishonest state simply
doesn't compile.

Binary determinism is the *hardware* version of exactly that dishonesty, forced by the substrate
instead of the type system. A bit that's "really" a noisy analog voltage, clamped hard to 0 or 1
by circuitry built to suppress what it actually is — that's `default(Result<T>)` at the silicon
layer: a state the system insists is well-formed because insisting is cheaper than being honest
about it, until you look at the power bill. Stochastic computing is the hardware world arriving,
decades late, at the same conclusion this codebase's `Outcome<T>` already forces at the type
level: **represent the uncertainty that's actually there, structurally, instead of suppressing it
and pretending afterward that you didn't.** Two unions, two substrates, one law, discovered twice
independently — which is usually a decent sign the law is real.

## What the call actually is

Not "everyone should throw away digital computing" — deterministic, exact silicon is still
correct for the workloads it was built for, the same way `Result<T>`'s ordinary composition is
still correct for scalar parsing that doesn't need the interior-event semantics `Outcome<T>`
carries. The call is narrower and more actionable:

- **To the people building probabilistic hardware right now**: the argument for why this matters
  doesn't have to stay implicit or purely energy-cost-shaped. It has a clean, already-proven
  software analog — a type system that got real, measurable engineering benefit (fewer classes of
  bug, forced honesty about failure, zero silent fallback) purely by refusing to represent false
  certainty. That's a story worth telling the same way to the people who'll fund and adopt the
  hardware version of it.
- **To software engineers who haven't thought about the hardware layer at all**: the discipline
  you already respect in a well-typed error-handling system — Rust's `Result`, this platform's
  `Outcome<T>`, any language's honest-about-failure union — is the same discipline the industry
  needs to extend one layer down, into the physical substrate those types eventually run on. If
  refusing to fake certainty in software was worth the redesign, refusing to fake it in silicon is
  the same argument at a different altitude.
- **To anyone deciding where the next decade of AI infrastructure investment goes**: better
  sampling, lower energy cost, and models that aren't fighting their own hardware to approximate
  randomness they need on purpose — that's not a niche research bet, it's the same efficiency
  argument that's driven every real hardware transition this industry has made, aimed at exactly
  the part of modern AI workloads that's growing fastest.

The honest version of a computer, for the part of the workload that's honestly probabilistic, is
one that stops pretending it isn't. This platform bet on that sentence in software months ago,
without originally meaning it as a hardware argument at all. It turns out to be one anyway.
