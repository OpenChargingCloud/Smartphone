# Smartphone

The smartphone of an EV driver in the OpenChargingCloud: a
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node) that stands in for
the apps on Android and iOS, with a JSON API below `/api` and a web interface
at `/`.

On top of what every node of the family is - the sign-in, DNS, NTS, the SSH
server, the certificate store and the log - it has a map of charging stations
and charging sessions started from it, simulated until there is an account at
an e-mobility provider to start real ones with. The map shows demo stations,
or - given the trust anchor of a roaming network - its charging pools, the
proof of each station checked against that anchor with
[WWCP_POI](https://github.com/OpenChargingCloud/WWCP_POI) before anything it
says is shown: its tariffs, its price statements, and the public keys its
energy meters sign their measurements with. With an account at an e-mobility
provider - an account key made on the phone, certified from a PKCS#10 request
- every start has a charging ticket of its own, built and checked with the
CBOR and COSE of Styx. Its store keeps what an app has to decide about trust:
the root CAs it believes, the server certificates it holds a server to, who it
is as a client, and who its web interface is over HTTPS.

It is not built alone: [SmartphoneCLI](https://github.com/OpenChargingCloud/SmartphoneCLI)
pins it beside the libraries it needs, builds it, and runs its tests.

| | |
|---|---|
| `Smartphone/Smartphone.cs` | the node: its names, its port (2346), the certificates it keeps, the security headers its pages need for the map |
| `Smartphone/SmartphoneAccess.cs` | the resources `map` and `charging`, and the role `driver` |
| `Smartphone/Map/` | where the map takes its stations from, the stations as OCPI locations, and the demo ones |
| `Smartphone/Roaming/` | the trust anchor of a roaming network, asking it at its URLs, and the check of a station's proof and price statements |
| `Smartphone/Provider/` | the account at an e-mobility provider, its client, and charging tickets: their payload, the request signed by the ticket key and the account key, and the check of what comes back |
| `Smartphone/Charging/` | a simulated charging session, and the file every session is kept in |
| `Smartphone/HTTPAPI/` | the JSON API: every node's, and `v1/map`, `v1/map/locations/{id}/check`, `v1/configuration/map`, `v1/configuration/roamingNetwork`, `v1/charging/sessions` and `v1/provider` |
| `Smartphone/Frontend/` | the web interface - the map is Leaflet's - bundled by webpack with what every node shares |
| `SmartphoneTests/` | what kind of node it is, the map and the sessions, the API with its roles, a roaming network served on the loopback and checked against its trust anchor, an e-mobility provider on the loopback issuing tickets, and WWCP_Node's conformance suite asked of a smartphone |
| `docs/` | drafts: starting a session with a charging ticket, signed by its key and countersigned by the provider |


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
