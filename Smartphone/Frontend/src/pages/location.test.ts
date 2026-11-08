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
        assert.doesNotMatch(root.querySelector('.price-statements')!.textContent!, /not in force/);

        assert.match(text, /Holders attesting\s*1 of the 1 required/);
        assert.match(text, /Holds until/);

    });

    it('says where a proof holds by no key trusted here, and where they are trusted', async () => {

        answer = () => ({ ...locationCheck(), verified: false,
                          stations: [ stationCheck('DE*GEF*S1', 'DE*GEF*E1*1', false, {
                                          error: "The proof of DE*GEF*S1 is not checked: none of the identity keys of the trust anchor of 'QA' is trusted here yet.",
                                          trust: { signaturesValid: true, signersTrusted: false, quorumReached: false, withinValidity: true, trustedHolders: 0, requiredConsensus: 1,
                                                   attestedBy: [], validUntil: null } }) ] });

        const root = await open(locationPage, '/map/locations/DE*GEF*P1', [ 'map:read' ],
                                smartphone, root => root.querySelector('.station-check-card') !== null);

        const hint = root.querySelector<HTMLElement>('.station-check-card .trust-hint');

        assert.ok(hint, 'it is not said where the keys are trusted');
        assert.match(hint.textContent!, /of 0 of the 1\s+holder\(s\) required/);
        assert.match(hint.querySelector('a')!.getAttribute('href')!, /\/configuration\/map$/);

    });

    it('says where price statements contradict each other, and that the price is not known', async () => {

        answer = () => ({ ...locationCheck(),
                          stations: [ stationCheck('DE*GEF*S2', 'DE*GEF*E2*1', true, {
                                          priceConflict: 'The price statements of DE*GEF*S2 do not hold together, and none of them is taken: Two price statements of one sequence of \'EVSE:DEGEFE21\': a split view!' }) ] });

        const root = await open(locationPage, '/map/locations/DE*GEF*P1', [ 'map:read' ],
                                smartphone, root => root.querySelector('.price-conflict') !== null);

        assert.match(root.querySelector('.price-conflict')!.textContent!, /a split view!.*What charging here costs is not known/s);

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

    it('names the web payment page of the operator of a station, and links it only where it is https', async () => {

        answer = () => {
            const check = locationCheck();
            check.stations[0]!.webPayment = { url: 'https://pay.gef.example/pools/P1', of: 'chargingPool',    error: null };
            check.stations[1]!.webPayment = { url: null, of: 'chargingStation', error: "'http://pay.gef.example/S2' is no https URL, so it is not offered." };
            return check;
        };

        const root  = await open(locationPage, '/map/locations/DE*GEF*P1', [ 'map:read' ],
                                 smartphone, root => root.querySelectorAll('.checked-evse').length === 2);

        const cards = [ ...root.querySelectorAll<HTMLElement>('.station-check-card') ];
        const link  = cards[0]!.querySelector<HTMLAnchorElement>('a.web-payment');

        assert.equal(link?.getAttribute('href'), 'https://pay.gef.example/pools/P1', 'the web payment page is not linked');
        assert.match(cards[0]!.textContent!, /Web payment\s*https:\/\/pay\.gef\.example\/pools\/P1\s*of its pool/);
        assert.equal(cards[1]!.querySelector('a.web-payment'), null, 'a page that is no https URL is linked');
        assert.match(cards[1]!.textContent!, /is no https URL, so it is not offered/);

    });

    it('says when there is nothing to check', async () => {

        const root = await open(locationPage, '/map/locations/nowhere', [ 'map:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        await until(() => /could not be checked/.test(root.textContent!), 'the refusal was not said');

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging station 'nowhere'/);

    });

});
