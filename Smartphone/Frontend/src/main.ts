import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { html } from '@node/view';
import { nodeMenu, startNode } from '@node/start';

import { configurationPage } from './pages/configuration';

// A smartphone has no page of its own yet - its account at an e-mobility
// provider, its charging tickets and the session it watches come later.
// Everything here is what every node has: its configuration, name resolution,
// the time, the SSH server, the certificates it believes, who it is as a
// client and the log - see WWCP_Node's start.ts.
startNode({

    name:  'Smartphone',
    icon:  'fa-mobile-screen-button',

    menu: [
        nodeMenu.configuration([
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.ssh,
            nodeMenu.certificates,
            nodeMenu.identities
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
            `
        }
    },

    // "/" is every node's: the first page of the menu the person signed in may
    // open - the configuration for whoever may read it.
    pages: {

        '/configuration':  configurationPage

    }

});
