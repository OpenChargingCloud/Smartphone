import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

// Leaflet's own stylesheet, for the map: extracted with the rest, its images
// hashed assets as well - nothing of it is loaded from elsewhere.
import 'leaflet/dist/leaflet.css';

import { html } from '@node/view';
import { nodeMenu, startNode } from '@node/start';

import { chargingPage } from './pages/charging';
import { configurationPage } from './pages/configuration';
import { historyPage } from './pages/history';
import { locationPage } from './pages/location';
import { mapPage } from './pages/map';
import { mapSettingsPage } from './pages/mapSettings';
import { providerPage } from './pages/provider';
import { sessionPage } from './pages/session';

// What a smartphone has pages for of its own: the map of charging stations,
// with what is signed about one of a roaming network, and charging - what
// charges now, every session there was, and one of them in full - with where
// the map takes its stations from and the account at the e-mobility provider
// below the configuration. Everything else is what every node has: name
// resolution, the time, the SSH server, the certificates it believes, who it
// is as a client and the log - see WWCP_Node's start.ts.
startNode({

    name:  'Smartphone',
    icon:  'fa-mobile-screen-button',

    menu: [
        { path: '/map',       label: 'Map',       icon: 'fa-map-location-dot', permission: [ 'map:read' ] },
        { path: '/charging',  label: 'Charging',  icon: 'fa-bolt',             permission: [ 'charging:read' ], children: [
            { path: '/charging/history', label: 'History', icon: 'fa-clock-rotate-left', permission: [ 'charging:read' ] }
        ] },
        nodeMenu.configuration([
            { path: '/configuration/map', label: 'Map', icon: 'fa-map', permission: [ 'map:read' ] },
            { path: '/configuration/provider', label: 'E-mobility provider', icon: 'fa-id-card', permission: [ 'provider:read' ] },
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.ssh,
            nodeMenu.certificates,
            nodeMenu.identities,
            nodeMenu.serverIdentities
        ]),
        nodeMenu.logs
    ],

    // What the roots it believes are for, in a driver's words: the servers an
    // app talks to - the time and name servers now, its e-mobility provider
    // later - and nothing at the other end of a charging cable, which is the
    // vehicle's business.
    certificates: {
        hints: {
            believes:  html`
                What this smartphone believes, beside the roots of this machine: a time server, a name server
                and later the e-mobility provider it signs in at are believed when they chain to one of these -
                or, held to a fingerprint, when they show exactly that certificate.
            `,
            presents:  html`
                Who this smartphone is to a server that asks, with its private key. Kept for when it signs in to
                its e-mobility provider with a certificate; nothing here presents it yet.
            `,
            serves:    html`
                Who this web interface is over HTTPS, with its private key: what --https shows a browser, or
                "web.tls.certificate": "store". A browser says where it is - Locate me - only to a page served
                over HTTPS or from the machine itself.
            `
        }
    },

    // "/" is every node's: the first page of the menu the person signed in may
    // open - the map, for whoever may look at it.
    pages: {

        '/map':                     mapPage,
        '/map/locations/:id':       locationPage,

        '/charging':                chargingPage,
        '/charging/history':        historyPage,
        '/charging/sessions/:id':   sessionPage,

        '/configuration':           configurationPage,
        '/configuration/map':       mapSettingsPage,
        '/configuration/provider':  providerPage

    }

});
