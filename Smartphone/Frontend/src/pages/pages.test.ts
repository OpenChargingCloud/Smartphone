/**
 * What the smartphone's own pages are held to: what every page of every kind
 * of node is, by the rules of WWCP_Node's test/pages.ts - the map's settings
 * have a form, and the other pages none: the map's search and its choice of a
 * charge point are inputs that keep nothing worth asking about.
 *
 * Run with `npm test`. "@node/.." is WWCP_Node/Frontend, where the rules are.
 */

import { everyPageIn } from '@node/../test/pages.ts';


everyPageIn(new URL('./', import.meta.url), {
    withForms: [ 'mapSettings.ts' ]
});
