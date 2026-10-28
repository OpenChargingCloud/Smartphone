/**
 * The map, drawn in a document of happy-dom against a stand-in smartphone:
 * a pin per station in the colour of what it is, the search finding stations
 * and opening one, and "Charge now!" asking the smartphone to charge at the
 * charge point chosen - or saying why it would not. Of a roaming network, a
 * station checked as its pin opens, and charged at only once its proofs hold.
 *
 * Leaflet draws into happy-dom as into a browser without a screen: there are
 * pins and popups, and no tiles that are ever loaded.
 */

import { asked, leave, open, refused, type as typeInto, until, type Asked } from '../../test/smartphone.ts';
import { holzmarkt, lobeda, locationCheck, mapData, paradies, roamingMapData, session } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { mapPage } = await import('./map.ts');

after(() => leave());


let startAnswer: unknown = undefined;

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === '/map')
        return mapData();

    if (method === 'POST' && path === '/charging/sessions')
        return startAnswer ?? new Response(JSON.stringify(session()), { status: 201, headers: { 'Content-Type': 'application/json' } });

    return undefined;

}

const pins = (root: HTMLElement) => [ ...root.querySelectorAll<HTMLElement>('.leaflet-marker-icon.station-marker') ];


describe('the map', () => {

    it('draws a pin for every station, in the colour of what it is', async () => {

        const root = await open(mapPage, '/map', [ 'map:read', 'charging:read', 'charging:run' ],
                                smartphone, root => pins(root).length === 2);

        const classes = pins(root).map(pin => pin.className);

        assert.ok(classes.some(names => names.includes('status-available')), 'the station with a free point is not available');
        assert.ok(classes.some(names => names.includes('status-charging')),  'the busy station is not charging');
        assert.ok(pins(root).some(pin => pin.title === 'Jena Holzmarkt'));

        assert.match(root.querySelector('.map-legend')!.textContent!, /Demo stations/);
        assert.ok(root.querySelector('.leaflet-control-attribution')?.textContent?.includes('OpenStreetMap'), 'the tiles are not credited');

    });

    it('finds stations as they are typed, and opens the one chosen', async () => {

        const root = await open(mapPage, '/map', [ 'map:read', 'charging:read', 'charging:run' ],
                                smartphone, root => pins(root).length === 2);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, 'lobeda');

        await until(() => root.querySelectorAll('.map-result').length === 1, 'the search did not find the one station');

        assert.match(root.querySelector('.map-result')!.textContent!, /Lobeda Stadtteilzentrum/);
        assert.match(root.querySelector('.map-result')!.textContent!, /0 of 1 available/);

        root.querySelector<HTMLButtonElement>('.map-result')!.click();

        await until(() => root.querySelector('.station-popup h3')?.textContent === 'Lobeda Stadtteilzentrum', 'the station chosen did not open');

        // Nothing free there: nothing to charge at.
        assert.equal(root.querySelector<HTMLButtonElement>('.station-popup .charge-now')!.disabled, true);
        assert.match(root.querySelector('.station-popup')!.textContent!, /No charge point here is available/);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, 'nowhere at all');

        await until(() => /No station has "nowhere at all"/.test(root.textContent!), 'a search that finds nothing did not say so');

    });

    it('charges at the free point of a station on "Charge now!"', async () => {

        startAnswer = undefined;

        const root = await open(mapPage, '/map', [ 'map:read', 'charging:read', 'charging:run' ],
                                smartphone, root => pins(root).length === 2);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, 'holzmarkt');
        await until(() => root.querySelector('.map-result') !== null, 'the search did not find the Holzmarkt');
        root.querySelector<HTMLButtonElement>('.map-result')!.click();

        await until(() => root.querySelector('.station-popup .charge-now') !== null, 'the Holzmarkt did not open');

        const popup  = root.querySelector<HTMLElement>('.station-popup')!;
        const radios = [ ...popup.querySelectorAll<HTMLInputElement>('input[type="radio"]') ];

        assert.equal(radios.length, 2);
        assert.equal(radios[0]!.checked,  true,  'the free point is not chosen');
        assert.equal(radios[1]!.disabled, true,  'the point out of order may be chosen');
        assert.match(popup.textContent!, /out of order/);
        assert.match(popup.textContent!, /Type 2 - 22 kW/);

        popup.querySelector<HTMLButtonElement>('.charge-now')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === '/charging/sessions'), '"Charge now!" did not ask the smartphone');

        assert.deepEqual(asked.find(one => one.method === 'POST')!.body, { locationId: holzmarkt().id, evseUid: '1' });

    });

    it('says why the smartphone would not charge, in the station', async () => {

        startAnswer = refused(409, 'DE*SST*E2001*1 at Jena Holzmarkt is charging, not available.');

        const root = await open(mapPage, '/map', [ 'map:read', 'charging:read', 'charging:run' ],
                                smartphone, root => pins(root).length === 2);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, 'holzmarkt');
        await until(() => root.querySelector('.map-result') !== null, 'the search did not find the Holzmarkt');
        root.querySelector<HTMLButtonElement>('.map-result')!.click();
        await until(() => root.querySelector('.station-popup .charge-now') !== null, 'the Holzmarkt did not open');

        root.querySelector<HTMLButtonElement>('.station-popup .charge-now')!.click();

        await until(() => root.querySelector('.station-popup .form-error') !== null, 'the refusal was not said');

        assert.match(root.querySelector('.station-popup .form-error')!.textContent!, /charging, not available/);

        startAnswer = undefined;

    });

    it('offers no "Charge now!" to whoever may only look', async () => {

        const root = await open(mapPage, '/map', [ 'map:read' ],
                                smartphone, root => pins(root).length === 2);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, String(lobeda().name));
        await until(() => root.querySelector('.map-result') !== null, 'the search did not find Lobeda');
        root.querySelector<HTMLButtonElement>('.map-result')!.click();
        await until(() => root.querySelector('.station-popup') !== null, 'Lobeda did not open');

        assert.equal(root.querySelector('.station-popup .charge-now'), null);
        assert.match(root.querySelector('.station-popup')!.textContent!, /may look at the map but not start charging/);

    });

});


describe('the map of a roaming network', () => {

    let verified = true;
    let checked  = false;

    function network({ method, path }: Asked): unknown {

        if (method === 'GET' && path.startsWith('/map/locations/') && path.endsWith('/check')) {
            checked = true;
            return locationCheck(verified);
        }

        if (method === 'GET' && path.startsWith('/map'))
            return roamingMapData([ checked && verified ? paradies({ verified: true, tariff: { price_per_kwh: 0.49, currency: 'EUR' } }) : paradies() ]);

        if (method === 'POST' && path === '/charging/sessions')
            return new Response(JSON.stringify(session()), { status: 201, headers: { 'Content-Type': 'application/json' } });

        return undefined;

    }

    async function opened(): Promise<HTMLElement> {

        const root = await open(mapPage, '/map', [ 'map:read', 'charging:read', 'charging:run' ],
                                network, root => pins(root).length === 1);

        typeInto(root.querySelector<HTMLInputElement>('#map-search')!, 'paradies');
        await until(() => root.querySelector('.map-result') !== null, 'the search did not find the Paradiesbahnhof');
        assert.match(root.querySelector('.map-result')!.textContent!, /2 charge point\(s\), their status not known/);
        root.querySelector<HTMLButtonElement>('.map-result')!.click();

        return root;

    }

    it('checks a station as it opens, and charges there once its proofs hold', async () => {

        verified = true;
        checked  = false;

        const root = await opened();

        await until(() => root.querySelector('.station-popup .chip.ok') !== null, 'the station was not checked as it opened');

        const popup = root.querySelector<HTMLElement>('.station-popup')!;

        assert.ok(asked.some(one => one.path === `/map/locations/${encodeURIComponent('DE*GEF*P1')}/check`), 'the check was not asked of the smartphone');
        assert.match(popup.textContent!, /attested by Open Charging Cloud\s+of Quality Assurance/);
        assert.match(root.querySelector('.map-legend')!.textContent!, /From the roaming network Quality Assurance/);
        assert.ok(popup.querySelector('a.station-details')?.getAttribute('href')?.endsWith('/map/locations/DE*GEF*P1'), 'no way to what is signed about it');

        await until(() => /0[.,]65/.test(root.querySelector('.station-popup')!.textContent!), 'the price its proof gives was not shown at its EVSE');

        const charge = root.querySelector<HTMLButtonElement>('.station-popup .charge-now')!;

        assert.equal(charge.disabled, false, 'no "Charge now!" at a station whose proofs hold');

        charge.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === '/charging/sessions'), '"Charge now!" did not ask the smartphone');

        assert.deepEqual(asked.filter(one => one.method === 'POST').pop()!.body, { locationId: 'DE*GEF*P1', evseUid: 'DE*GEF*E1*1' });

    });

    it('says when a station is not signed as the trust anchor requires, and charges nothing there', async () => {

        verified = false;
        checked  = false;

        const root = await opened();

        await until(() => root.querySelector('.station-popup .chip.bad') !== null, 'a station whose proofs do not hold was not said to be unsigned');

        assert.match(root.querySelector('.station-popup')!.textContent!, /not attested by the operator the trust anchor names/);
        assert.match(root.querySelector('.station-popup')!.textContent!, /the price is what its proof says/);
        assert.equal(root.querySelector<HTMLButtonElement>('.station-popup .charge-now')!.disabled, true, '"Charge now!" at a station nobody it trusts attested');

    });

});
