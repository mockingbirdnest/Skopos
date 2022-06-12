# Σκοπός
> ὁ **σκοπός, οῦ**: goal.

KSP contracts that specify goals, and leave mission design to the player.

## Why Σκοπός?

The stock career mode, as well as modded careers inspired by the stock system,
_e.g._, [RP-1](https://github.com/KSP-RO/RP-0), have some contracts focused on
ultimate mission goals such as

> achieving the goal, before this decade is out, of landing a man on the Moon
> and returning him safely to the Earth

which allow for many different mission profiles: direct ascent, earth orbit
rendez-vous, lunar orbit rendez-vous, etc.

However, many contracts instead force a mission profile onto the player;
requiring that some experiments be made in specific altitude ranges, or even
requiring that satellites be placed into specific orbits.

These contracts can quickly become dull and repetitive, as there is no tangible
reason for the requested orbit, and the whole mission becomes a mere exercise in
precision in orbital insertion.
Such contracts can also result in messy interactions with mods such as Principia
that perturb the orbits.

Σκοπός aims to provide a framework allowing these constrained contracts to be
turned into goal-oriented contracts.

For instance, instead of a contract to put a communications satellite in
geostationary orbit over a specific latitude, the goal would be to provide a
sustained transatlantic connection—by any means necessary, whether it be an MEO
or LEO constellation or a single geosynchronous satellite.

Similarly, instead of having a contract to put a specific imaging instrument in
a specific orbit, the goal would be to image a target at a specific resolution,
possibly with time or freshness contraints, whether with a small telescope on an
aircraft or a larger one on a satellite in low circular orbit or in an
elliptical orbit.

Likewise contracts for positioning systems would have goals based on the accuracy
of positioning, rather than on the orbits of the satellite in the system, goals
for space observatories would reflect what they observe, rather than where they
are, etc.

## Status

The current prototype focuses exclusively on telecommunications. It is based on
RP-1, but does not integrate with the RP-1 career; the current focus is instead
to gather data on the solutions players come up with, so as to inform balance: it
would be pointless to have theoretically flexible contracts that are balanced in
such a way as to allow only one economically viable solution.

## Historical notes

An earlier iteration of this idea was implemented by
[@SirMortimer](github.com/SirMortimer), first under the name of
KerbalismContracts, then under the name Σκοπός.

