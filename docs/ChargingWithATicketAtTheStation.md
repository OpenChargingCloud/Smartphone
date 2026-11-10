# Charging with a ticket shown at the station (draft)

How an EV driver with a contract at an **e-mobility provider (EMP, EMSP)**
charges with a **charging ticket** the app fetched from the EMP beforehand and
shows to the station itself - over **NFC** or **Bluetooth Low Energy (BLE)** -
so that the station starts on the ticket, without the EMP in between at the
start, and where it has to, without being online.

It is the same ticket as for charging
[through the EMP](ChargingThroughTheEMP.md): one per start, with a key of its
own made on the phone, signed by the EMP - the CPO learns which EMP pays, and
not who charges. What differs is the way to the station: not app → EMP → OCPI
→ CPO → station, but app → station. That takes away what the EMP adds today at
the start - its countersignature, its token, its real-time "yes" - and asks
the station to decide on what the ticket says alone. Most of this document is
about what replaces it.

**Status: a draft - a design, not a description.** Of all of this only the
ticket exists: the app gets one per start from the EMP, right before the start
([Step 2 there](ChargingThroughTheEMP.md#step-2---app--emp-a-charging-ticket-for-this-start)).
No ticket fetched beforehand, no NFC, no BLE, no station that checks a ticket.
Steps are marked by who does them - **[App]**, **[EMP]**, **[Station]**,
**[CSMS]**, **[CPO]** -, and what is not settled is marked **Open** and
collected under [Gaps and open points](#gaps-and-open-points).


## Who is offline

"Offline" is said of two things, and the cases differ more than they look:

| | The phone | The station | Who decides on the ticket | Who vouches for the start |
|---|---|---|---|---|
| **1 - the phone offline** | no network, e.g. in an underground car park | reaches its CSMS | the CSMS, asking the EMP in real time | the EMP, by its answer, and the ticket |
| **2 - the station offline** | online, or not | does not reach its CSMS | the station alone | the ticket alone: the EMP's promise to pay up to its limits |
| **3 - both offline** | no network | does not reach its CSMS | the station alone | the ticket alone |

Case 1 is the near one: a station that reaches its CSMS can have it check the
ticket and ask the EMP, much as for a start through the EMP. Cases 2 and 3 are
the hard ones, and the reason for this document: a station that checks a
ticket itself has to hold the EMP's keys and a list of withdrawn tickets, and
the EMP carries the risk of every ticket it issued until the station learns it
was withdrawn.

The phone being offline matters on its own as well: it can not get a ticket
then, nor the station's proof - so it fetches tickets beforehand, and checks
the station afterwards.


## The parties and their keys

| Who | Key | What it is trusted for |
|---|---|---|
| **The driver's account at the EMP** | an **account key**, P-256, made on the phone, long-lived | "this is me" - to the EMP alone, which certified it |
| **One start** | a **ticket key**, P-256, made on the phone for one ticket and thrown away after its start | "I hold this ticket" - to the station, as the ticket names the key and the EMP signed the ticket |
| **The EMP** | its **ticket issuer key** | "this EMP issued this ticket and pays for a start on it, up to its limits" - to the station, which has to hold it beforehand |
| **The station** | a **station key** - **Open:** WWCP_POI attests none | "I am this station, of this proof" - to the app, before it hands over a ticket |
| **The roaming network operator (RNO)** | its identity keys, by the thumbprints in the trust anchor | what the network says of the station - to the app; and, **Open**, the EMP's ticket issuer key - to the station |
| **The station's energy meter** | its signing key, attested by the RNO in the station's proof | the signed meter values (OCMF) - to the app, checked with the attested key only |

The account key never reaches the station. The ticket key never leaves the
phone. The meter's key signs measurements and nothing else: it is no key for
the station to prove itself with.


## How it differs from charging through the EMP

| | Through the EMP | A ticket shown at the station |
|---|---|---|
| the ticket | fetched right before the start, good for an hour | fetched beforehand, good for days - **Open:** how many |
| its limits | none yet | **required**: they are all the EMP promised |
| the way to the station | app → EMP → OCPI `START_SESSION` → CSMS → station | app → station, over NFC or BLE |
| the start request | signed by the ticket key and countersigned by the EMP, with a token of this start | signed by the ticket key alone, with a challenge of the station |
| the token | made by the EMP for this start | the ticket's id - **Open:** as an OCPP and OCPI token |
| who says yes | the EMP, in real time | case 1: the EMP, asked by the CSMS; cases 2 and 3: the station, by the ticket alone |
| a withdrawn ticket | refused by the EMP at once | refused by a station once it learned it |
| which station the app talks to | the EVSE the driver picked; the EMP names it to the CPO | **Open:** the station has to prove it |
| signed meter values reach the app | after it ended, in the CDR | over BLE while it charges; else in the CDR, once the station is online again |
| stopping | a stop request, countersigned by the EMP | a stop request over NFC or BLE, signed by the ticket key alone; or unplugging |
| the price checked | the price statement in force at the start, against the CDR | the same, once the app is online and has the CDR |


## Overview

Case 2, the station offline:

```mermaid
sequenceDiagram
    autonumber
    actor D as Driver
    participant A as App
    participant E as EMP
    participant N as Roaming network
    participant S as Station and meter
    participant C as CSMS (CPO)

    rect rgba(127,127,127,0.1)
    Note over A,E: beforehand, online
    A->>E: tickets, each with a key of its own, signed by the account key
    E-->>A: the tickets, signed by the EMP
    C->>S: the EMP's ticket issuer keys, withdrawn tickets (while online)
    end

    D->>A: picks the EVSE at the station
    A->>S: NFC tap or BLE: hello
    S-->>A: its challenge, signed by the station key
    A->>A: the station is the one of its proof?
    A->>S: ticket + start request (ticket key, EVSE, challenge)
    S->>S: ticket of an EMP it holds keys of, not withdrawn, not used; start request holds
    S->>S: starts, limits of the ticket; meter signs ID = ticket id
    loop while it charges (BLE)
        S-->>A: signed meter values
    end
    D->>A: Stop charging
    A->>S: stop request, signed by the ticket key
    S-->>A: signed end value
    Note over S,C: once online again
    S->>C: the transaction, offline = true, with the ticket and the start request
    C->>E: CDR, its token the ticket id
    A->>E: what became of the ticket (once online)
    E-->>A: the CDR with its signed meter values
    A->>N: proofs and price statements of the time
    A->>A: meter values and price checked
```


## Step 0 - Once: a roaming network and an account at the EMP

As for charging through the EMP:
[trusting a roaming network](ChargingThroughTheEMP.md#step-0---once-trusting-a-roaming-network)
and [an account at the EMP](ChargingThroughTheEMP.md#step-1---once-an-account-at-the-emp),
with an account key certified from a PKCS#10 request.


## Step 1 - [App ↔ EMP] Tickets, beforehand

While the phone is online, it keeps a few tickets at hand:

1. **[App]** makes a ticket key for each, and writes each ticket as today
   ([the draft, 2](RemoteStartWithChargingTickets.md#2-the-ticket-one-per-start)),
   signed by its ticket key and the account key - but good for days, not an
   hour, and **with limits**.
2. **[EMP]** checks, takes the account's signature off, signs with its ticket
   issuer key, and keeps which account each ticket is of.
3. **[App]** checks what comes back, and keeps the tickets with their keys.
   One is used for one start, and thrown away after it, with its key.

**Limits are required.** Through the EMP, the EMP says yes to every start in
real time and can say no; a station that decides alone has nothing but the
ticket. So the ticket is the EMP's promise to pay for one start **up to its
limits** - energy, time, power, or a cost - and a ticket without limits a
promise without end.

**Nothing that tells drivers apart:** every ticket of every account with the
same few limits and the same validity, its `nbf` the start of the day it was
fetched - not the second, which would be a fingerprint of its own.

**Few at a time.** Every ticket on the phone is a promise the EMP keeps until
every station learned it was withdrawn: a lost phone, or a ticket key that
leaked, costs up to the limits of every ticket on it. **Open:** how many, and
good for how long - a few, for a week, say.

**Open:** the station's **proof** for the app as well. A phone that is offline
at the station can not check it there; it may keep the proofs of stations it
looked at, but their attestation holds for about a quarter of an hour. It
checks the station afterwards, by the proofs of the time
([Step 7](#step-7---app-the-signed-meter-values-and-the-price)).


## Step 2 - [App ↔ Station] Which station

The app has to know which station it talks to before it hands over a ticket:
a start request names one EVSE, and whoever gets it started there charges at
the driver's cost.

- **NFC:** the driver holds the phone to the station's reader - the one for
  RFID cards. Distance proves something: a few centimetres, at that station.
  The phone acts as a card (host card emulation), with an application of its
  own (an AID) the station's reader has to know; on Android, and on iOS where
  Apple allows it.
- **BLE:** the station advertises a service of its own; the app finds the
  stations near and their EVSEs, and the driver picks the EVSE printed on the
  charge point. Anything in range can advertise the same: a box beside the
  station could name its EVSE, pass the start request on to it, and have the
  driver pay for another car.

So the station proves itself:

1. **[App]** sends a nonce.
2. **[Station]** answers with its **challenge** - a nonce of its own - its
   station id, its EVSEs, and its clock, signed by its **station key** over
   both nonces.
3. **[App]** checks the signature with the station key the proof of the
   station attests - where it has a proof that holds; else it keeps the answer
   and checks it afterwards, against the proofs of the time.

**Open:** WWCP_POI attests no station key. A station has the public keys its
meters sign measurements with, and those must sign nothing else. A key of a
station of its own - a usage such as `stationIdentity`, or a certificate of it
below a key of the operator - would be new.

Even then, a relay that passes NFC or BLE on to another station of the same
proof could fool the app; only measuring the distance would not be fooled.
Where a station proves nothing, NFC is better than BLE, and the app says that
the station is not proven.


## Step 3 - [App → Station] The ticket and the start request

The **start request** is the one of the draft
([3](RemoteStartWithChargingTickets.md#3-the-start-request-signed-by-the-ticket-key-and-by-the-emsp)),
signed by the ticket key alone, with one more field:

```
{
  "typ":        "StartRequest",
  "v":          1,
  "ticket":     h'SHA-256 of the ticket as the EMP issued it',
  "evse":       "DE*GEF*E2*1",
  "at":         1793200000,
  "nonce":      h'16 random bytes',
  "challenge":  h'the challenge of the station',       ; new: once-only without the station's clock
  "proof":      h'the effectiveTree digest the app checked',   ; optional
  "tariff":     "DE*GEF*T_DC"                                  ; optional
}
```

The challenge makes the request one the station asked for just now, and of
no use anywhere else - without the station's clock being right, which offline
it may well not be.

**[App]** sends the ticket and the start request: as CBOR over a
characteristic of its BLE service, or as APDUs over NFC - a ticket and a
start request are a few hundred bytes each, more than one APDU holds, so in
chained or extended APDUs.

**Open:** the BLE service, the NFC application, and how both carry CBOR - no
standard says any of it.


## Step 4 - [Station] Checks, and the start

**Case 2 and 3 - the station alone** checks:

1. **the ticket:** signed by its ticket key and by a ticket issuer key it holds
   for the EMP the ticket names (`emsp`), good now as far as it can tell;
2. **not withdrawn:** not on its list of withdrawn tickets;
3. **not used:** not one it started on before - it keeps the ids of the tickets
   it took until their `exp`;
4. **the start request:** its `ticket` the digest of this ticket, signed by the
   ticket's key, its `evse` one of its own, its `challenge` the one it just
   gave;
5. **the limits:** within what it takes from that EMP; they become the
   station's, as it keeps to a `transactionLimit`.

It starts the transaction with the **ticket's id** as its `idToken`, and its
meter signs that id as the identification of every value (OCMF `ID`) - nothing
of the account, as through the EMP. It keeps the ticket and the start request
with the transaction: its evidence that the EMP promised to pay, and that the
holder of the ticket asked for this start at this EVSE.

**Case 1 - the station online** passes the ticket and the start request on to
its CSMS, which checks the same and asks the EMP in real time - OCPI's
`POST …/tokens/{uid}/authorize`, its `uid` the ticket's id. **[EMP]** answers
`ALLOWED` for a ticket it issued, not withdrawn and not used, at that
location; `BLOCKED` otherwise. Its answer is not signed: the ticket stays the
evidence.

**[Station → App]** answers whether it started - or why not.

Where the station takes its keys and list from:

- **The EMP's ticket issuer keys:** from the roaming network, where the EMP is
  a party (WWCP_POI's `eMobilityProviders`) - **Open:** with a key usage for
  tickets, which WWCP_POI does not have - and from there to the station by its
  CSMS, while it is online. **Open:** OCPP has no message for it.
- **Withdrawn tickets:** OCPP's **Local Authorization List** takes idTokens
  with the status `Blocked`; the ticket's id would be one. The EMP tells the
  CPOs it is peered with; their CSMSs send it to their stations. A list of
  every withdrawn ticket of every EMP grows: a ticket that ran out (`exp`)
  can leave it.

**Open:**

- **The answer** of the station is not signed: the app knows it started from
  the meter's signed start value alone - over BLE, or later in the CDR.
- **The token:** OCPP has no `idToken` type for a ticket, nor OCPI a token
  type; and `OfflineTxForUnknownIdEnabled` - starting offline for anybody -
  is no substitute.


## Step 5 - [App] While it charges

- **Over BLE:** the station sends every signed meter value as it signs it,
  much as a live link does, and the app checks each as it comes - as ad hoc at
  the CPO
  ([Step 4 there](AdHocChargingAtTheCPO.md#step-4---checking-every-signed-meter-value-as-it-comes)),
  with the ticket's id in place of the payment reference - where it has a proof
  that holds; else it keeps the values and checks them afterwards. A wrong
  value stops the session: a stop request, as below.
- **Over NFC:** nothing while it charges. The values come at the next tap, or
  later in the CDR.
- **Online:** the app can ask the EMP what became of the ticket, as through
  the EMP ([Step 6 there](ChargingThroughTheEMP.md#step-6---app-while-it-charges)) -
  the EMP knows nothing of it before the station is online again.


## Step 6 - Stopping

- **By the driver:** a **stop request** as in the draft
  ([7](RemoteStartWithChargingTickets.md#7-stopping)) - the ticket, the start
  request it names, `at`, a nonce, and a new challenge of the station -,
  signed by the ticket key alone, over BLE or at a tap. **[Station]** checks it
  names the start it took and is signed by the ticket's key, and stops.
- **By unplugging,** the limit, the vehicle or the station: the station ends
  the transaction, and the meter signs the end with the reason (`TX`).
- **By the EMP:** not until the station reaches its CSMS. A stop through the
  EMP, as for a start through it, does not reach a station offline.

Then the station sends the signed end value over BLE, and the app checks it
as every value.


## Step 7 - [App] The signed meter values and the price

Once the station is online again, it reports the transaction, marked
`offline`, to its CSMS, with the ticket and the start request; the CPO sends
the CDR to the EMP, its token the ticket's id; the EMP knows the account and
bills it - as through the EMP, but later.

The app checks as through the EMP:
[the CDR and its signed meter values](ChargingThroughTheEMP.md#step-8---app-the-cdr-and-its-signed-meter-values)
and [the final price](ChargingThroughTheEMP.md#step-9---app-the-final-price),
the start document of the price the start request - and, where it came over
BLE, the signed values it has itself against those of the CDR. A phone that
was offline at the station checks now what it could not then: the station's
answer to its challenge, with the key its proof of that time attests.

**Open:** the CDR as OCPI has it carries neither the ticket nor the start
request; as for `START_SESSION`, fields of their own.


## Who carries the risk

| | The risk | What bounds it |
|---|---|---|
| **EMP** | a start on a ticket it withdrew, at a station that did not learn it yet; a ticket used twice, at two stations offline - only by whoever has its key | the limits of a ticket, the number of tickets on a phone, how long they are good, how soon stations learn of a withdrawn one |
| **CPO** | a start on a ticket of an EMP it does not hold keys of, or that it did not check | its station refuses it |
| **Driver** | a lost phone, with its tickets and their keys; a station that is not the one of its proof | the app's lock, and the EMP withdrawing them; the station key (Step 2) |

A ticket used twice shows at the EMP as two CDRs of one ticket id. It pays
both, as it promised, and withdraws the account's other tickets: the app never
uses one twice, so its key leaked.


## What it hides, and what it does not

As through the EMP
([there](ChargingThroughTheEMP.md#what-it-hides-and-what-it-does-not)): the
CPO learns the EMP, a ticket and its id, and not the driver. Beyond that:

- **The time a ticket was fetched** is in its `nbf`: the start of the day, not
  the second.
- **BLE** shows the phone's address to whoever listens near: it has to change
  it, as phones do.
- **The EMP** learns where its ticket was used only from the CDR, as before -
  and in case 1 from the CSMS asking.


## Gaps and open points

| | Where |
|---|---|
| How many tickets on a phone, good for how long, with which limits - the same for everybody | [Step 1](#step-1---app--emp-tickets-beforehand) |
| A station key in WWCP_POI, and the app checking it - or what a station proves without one | [Step 2](#step-2---app--station-which-station) |
| A BLE service and an NFC application for tickets, and how they carry CBOR | [Step 3](#step-3---app--station-the-ticket-and-the-start-request) |
| The `challenge` of the station in the start request and the stop request | [Step 3](#step-3---app--station-the-ticket-and-the-start-request) |
| A key usage for ticket issuers in WWCP_POI, and how a CSMS gives a station those keys | [Step 4](#step-4---station-checks-and-the-start) |
| Withdrawn tickets on the Local Authorization List: how they get there, and how long it grows | [Step 4](#step-4---station-checks-and-the-start) |
| A ticket's id as a token, in OCPP and in OCPI | [Step 4](#step-4---station-checks-and-the-start) |
| A signed answer of the station to a start | [Step 4](#step-4---station-checks-and-the-start) |
| The ticket and the start request in the CDR | [Step 7](#step-7---app-the-signed-meter-values-and-the-price) |
| What the EMP does with a ticket used twice | [Who carries the risk](#who-carries-the-risk) |


## What exists today

| | |
|---|---|
| EMP | tickets issued from a request signed by the ticket key and an account key, good for up to 30 days; real-time authorization of the tokens of one start through the EMP - not of a ticket's id |
| Smartphone | a ticket per start, good for an hour, without limits, built, signed and checked with Styx ([`Smartphone/Provider/ChargingTickets.cs`](../Smartphone/Provider/ChargingTickets.cs)); no tickets fetched beforehand, no NFC, no BLE - and, as it is a node that stands in for the apps, a transport that would only be simulated in its tests |
| Station, CSMS, CPO | nothing of it |
