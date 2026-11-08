# Smartphone

The smartphone of an EV driver in the OpenChargingCloud: a
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node) that stands in for
the apps on Android and iOS, with a JSON API below `/api` and a web interface
at `/`.

On top of what every node of the family is - the sign-in, DNS, NTS, the SSH
server, the certificate store and the log - it has a map of charging stations
and charging sessions started from it - simulated, or through an account at
an e-mobility provider and as the provider reports them, simulated only where
it reports nothing. The map shows demo stations,
or - given the trust anchor of a roaming network - its charging pools, which
of their charge points are free as the network says it over its WebSocket -
nothing anybody signs, and nothing kept once the network is not reached - the
proof of each station checked with
[WWCP_POI](https://github.com/OpenChargingCloud/WWCP_POI) before anything it
says is shown: its tariffs, its price statements, the public keys its
energy meters sign their measurements with, and the web payment page of its
operator, where a driver pays ad hoc - and the live link of the session that
page starts, handed over to the phone and followed with
[ChargyCore.NET](https://github.com/OpenChargingCloud/ChargyCore.NET), its
signed documents kept as they came. It is checked against those of
the anchor's identity keys an administrator trusted, each on its own, after
looking at the key statement of the network's operator; setting an anchor
trusts none of them. With an account at an e-mobility
provider - an account key made on the phone, certified from a PKCS#10 request
- every start has a charging ticket of its own, and the start and the stop are
requests signed by the ticket's key and countersigned by the provider, all of
it built and checked with the CBOR and COSE of Styx; what became of the start
is asked of the provider, and taken only where it is of that start. A session
at a station of a roaming network keeps the proofs of its station and the
price statements that held, and the signed meter values of its charge detail
record are checked with the key those proofs give for the meter of its EVSE
at the time each was measured - read and verified by
[ChargyCore.NET](https://github.com/OpenChargingCloud/ChargyCore.NET) - and
its price against the statement in force at its start. Its store keeps what an
app has to decide about trust:
the root CAs it believes, the server certificates it holds a server to, who it
is as a client, and who its web interface is over HTTPS.

It is not built alone: [SmartphoneCLI](https://github.com/OpenChargingCloud/SmartphoneCLI)
pins it beside the libraries it needs, builds it, and runs its tests.

| | |
|---|---|
| `Smartphone/Smartphone.cs` | the node: its names, its port (2346), the certificates it keeps, the security headers its pages need for the map |
| `Smartphone/SmartphoneAccess.cs` | the resources `map` and `charging`, and the role `driver` |
| `Smartphone/Map/` | where the map takes its stations from, the stations as OCPI locations, and the demo ones |
| `Smartphone/Roaming/` | the trust anchor of a roaming network and the identity keys of it trusted, asking the network at its URLs, the status of its charge points followed over its WebSocket, the key statement of its operator, and the check of a station's proof and price statements against a trust context of the keys trusted, with the web payment page it gives |
| `Smartphone/Provider/` | the account at an e-mobility provider, its client, charging tickets - their payload, the request signed by the ticket key and the account key, and the check of what comes back - start and stop requests, signed by the ticket key, and the check of the provider's countersignature, and what the provider reports of a start, checked to be of it |
| `Smartphone/Charging/` | a charging session - simulated, as the provider reports it, or as the live link of one the web payment page of its operator started says it - with its ticket and its start and stop through the provider, the evidence it keeps of its station and the check of its meter values and its price against it, the live link and the signed documents it brought, and the file every session is kept in |
| `Smartphone/HTTPAPI/` | the JSON API: every node's, and `v1/map`, `v1/map/locations/{id}/check`, `v1/configuration/map`, `v1/configuration/roamingNetwork` with its `keyStatement` and `trustedKeys/{id}`, `v1/charging/sessions` and `v1/provider` |
| `Smartphone/Frontend/` | the web interface - the map is Leaflet's - bundled by webpack with what every node shares |
| `SmartphoneTests/` | what kind of node it is, the map and the sessions, the API with its roles, a roaming network served on the loopback and checked against the identity keys of its trust anchor trusted - none before an administrator trusts one - with energy meters that sign OCMF, a web payment page of its operator and of a station, a charge point operator whose page gives the live link of the session it started and sends signed meter values over it, and the status of its charge points pushed over its WebSocket, an e-mobility provider on the loopback issuing tickets, countersigning starts and stops and telling what its operator reported of them, signed meter values among it - and misbehaving - and WWCP_Node's conformance suite asked of a smartphone |
| `docs/` | drafts: starting and stopping a session with a charging ticket, signed by its key and countersigned by the provider, what the app learns of it after the start, and how it checks the meter values and the price |


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
