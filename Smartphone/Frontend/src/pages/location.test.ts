/**
 * What is signed about a charging pool of a roaming network, drawn in a
 * document of happy-dom against a stand-in smartphone: who attested the proof
 * of each station and until when, each EVSE with its tariff and the price a
 * statement sets, the public key its energy meter signs with - and, of a
 * station whose proof does not hold, why, and nothing of what it says.
 */

import { leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { locationCheck, stationCheck } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { locationPage } = await import('./location.ts');

// Amounts as the browser's locale writes them: 0,65 € here, €0.65 there.
const { money }        = await import('../charging/words.ts');

after(() => leave());


let answer: () => unknown = () => locationCheck();

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === `/map/locations/${encodeURIComponent('DE*GEF*P1')}/check`)
        return answer();

    if (method === 'GET' && path.endsWith('/check'))
        return refused(404, "There is no charging station 'nowhere' on the map.");

    return undefined;

}


describe('what is signed about a station', () => {

    it('says who attested it, and gives each EVSE with its price and the key of its meter', async () => {

        answer = () => locationCheck();

        const root = await open(locationPage, '/map/locations/DE*GEF*P1', [ 'map:read' ],
                                smartphone, root => root.querySelectorAll('.checked-evse').length === 2);

        const text = root.textContent!;

        assert.ok(root.querySelector('h2 .chip.ok'), 'the pool is not said to be signed');
        assert.match(text, /Quality Assurance \(QA\)/);
        assert.match(text, /Open Charging Cloud/);
        assert.match(text, /OCC, as roamingNetworkOperator/);
        assert.match(text, /GraphDefined GD-DC300/);
        assert.ok(text.includes(`${money(0.65, 'EUR')} per kWh`), 'the price the statement sets is not given');
        assert.match(text, /Tariff DE\*GEF\*T_DC/);
        assert.ok(text.includes(`${money(0.69, 'EUR')}/kWh`), 'the price of the tariff is not given');
        assert.match(text, /Kernberg KDM-500/);
        assert.match(text, /DC-2026-001/);
        assert.match(text, /OCMF/);

        const key = root.querySelector('.meter-key .public-key')!;

        assert.equal(key.textContent, 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0J5uv5Eo22C9', 'the key of the meter is not given in full');
        assert.match(root.querySelector('.meter-key')!.textContent!, /secp256r1/);

        assert.match(root.querySelector('.price-statements')!.textContent!, /holds/);
        assert.match(root.querySelector('.price-statements')!.textContent!, /by DE\*GEF, number 0/);

    });

    it('says why a proof does not hold, and nothing of what it says', async () => {

        answer = () => ({ ...locationCheck(), verified: false,
                          stations: [ stationCheck('DE*GEF*S1', 'DE*GEF*E1*1'), stationCheck('DE*GEF*S2', 'DE*GEF*E2*1', false) ] });

        const root = await open(locationPage, '/map/locations/DE*GEF*P1', [ 'map:read' ],
                                smartphone, root => root.querySelectorAll('.station-check-card').length === 2);

        const cards = [ ...root.querySelectorAll<HTMLElement>('.station-check-card') ];

        assert.ok(root.querySelector('h2 .chip.bad'), 'the pool is said to be signed');
        assert.ok(cards[0]!.querySelector('.chip.ok'));
        assert.ok(cards[1]!.querySelector('.chip.bad'));
        assert.match(cards[1]!.querySelector('.error-box')!.textContent!, /not attested by the operator the trust anchor names/);
        assert.equal(cards[1]!.querySelector('.meter-key'), null, 'a key of a proof that does not hold is shown');

    });

    it('says when there is nothing to check', async () => {

        const root = await open(locationPage, '/map/locations/nowhere', [ 'map:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        await until(() => /could not be checked/.test(root.textContent!), 'the refusal was not said');

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging station 'nowhere'/);

    });

});
