# Smartphone

The smartphone of an EV driver in the OpenChargingCloud: a
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node) that stands in for
the apps on Android and iOS, with a JSON API below `/api` and a web interface
at `/`.

On top of what every node of the family is - the sign-in, DNS, NTS, the SSH
server, the certificate store and the log - it has a map of charging stations
and charging sessions started from it, simulated until there is an account at
an e-mobility provider to start real ones with. Its store keeps what an app
has to decide about trust: the root CAs it believes, the server certificates
it holds a server to, and who it is as a client.

It is not built alone: [SmartphoneCLI](https://github.com/OpenChargingCloud/SmartphoneCLI)
pins it beside the libraries it needs, builds it, and runs its tests.

| | |
|---|---|
| `Smartphone/Smartphone.cs` | the node: its names, its port (2346), the certificates it keeps, the security headers its pages need for the map |
| `Smartphone/SmartphoneAccess.cs` | the resources `map` and `charging`, and the role `driver` |
| `Smartphone/Map/` | where the map takes its stations from, the stations as OCPI locations, and the demo ones |
| `Smartphone/Charging/` | a simulated charging session, and the file every session is kept in |
| `Smartphone/HTTPAPI/` | the JSON API: every node's, and `v1/map`, `v1/configuration/map` and `v1/charging/sessions` |
| `Smartphone/Frontend/` | the web interface - the map is Leaflet's - bundled by webpack with what every node shares |
| `SmartphoneTests/` | what kind of node it is, the map and the sessions, the API with its roles, and WWCP_Node's conformance suite asked of a smartphone |


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
