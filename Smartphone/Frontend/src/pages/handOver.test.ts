/**
 * The page the web payment page of an operator sends the driver back to,
 * drawn in a document of happy-dom against a stand-in smartphone: it says
 * which charge point and where the hand-over is, takes nothing as it opens,
 * and hands the hand-over to the smartphone once the driver says so.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { paradies, roamingMapData, session } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { handOverPage, handOverTo, withHandOverTo } = await import('./handOver.ts');

after(() => leave());


let takeAnswer: unknown = undefined;

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === '/map')
        return roamingMapData([ paradies() ]);

    if (method === 'POST' && path === '/charging/sessions')
        return takeAnswer ?? new Response(JSON.stringify(session()), { status: 201, headers: { 'Content-Type': 'application/json' } });

    return undefined;

}

const address = (handOver: string | null, locationId = 'DE*GEF*P1', evseUid = 'DE*GEF*E1*1') =>
    '/charging/handOver?' + new URLSearchParams({ locationId, evseUid, ...(handOver !== null ? { handOver } : {}) }).toString();


describe('the hand-over a web payment page sends the driver back with', () => {

    it('names the page of this phone and the charge point paid for, and keeps what the web payment page asks already', () => {

        const to = new URL(handOverTo('https://phone.example:2346', 'DE*GEF*P1', 'DE*GEF*E1*1'));

        assert.equal(to.origin + to.pathname, 'https://phone.example:2346/charging/handOver');
        assert.deepEqual([ ...to.searchParams.entries() ], [ [ 'locationId', 'DE*GEF*P1' ], [ 'evseUid', 'DE*GEF*E1*1' ] ]);

        const page = new URL(withHandOverTo('https://pay.gef.example/stations/S1?lang=de', 'https://phone.example:2346', 'DE*GEF*P1', 'DE*GEF*E1*1'));

        assert.equal(page.searchParams.get('lang'),       'de');
        assert.equal(page.searchParams.get('handOverTo'), to.toString());

        assert.equal(withHandOverTo('no URL', 'https://phone.example:2346', 'DE*GEF*P1', 'DE*GEF*E1*1'), 'no URL');

    });

    it('takes nothing as it opens, and the hand-over once the driver says so', async () => {

        takeAnswer = undefined;

        const root = await open(handOverPage, address('https://pay.gef.example/handOvers/S1?key=77d0'), [ 'charging:read', 'charging:run' ],
                                smartphone, root => /Jena, Paradiesbahnhof/.test(root.textContent!));

        const card = root.querySelector<HTMLElement>('.hand-over-page')!;

        assert.match(card.textContent!, /DE\*GEF\*E1\*1/);
        assert.match(card.textContent!, /at\s+pay\.gef\.example/);
        assert.doesNotMatch(card.textContent!, /77d0/, 'the key of the hand-over is shown');
        assert.match(card.textContent!, /only where that is the host of the web payment page the proof of the station names/);
        assert.ok(!asked.some(one => one.method === 'POST'), 'the hand-over was taken as the page opened');

        card.querySelector<HTMLButtonElement>('button.take-over')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === '/charging/sessions'), 'the hand-over was not handed over');

        assert.deepEqual(asked.filter(one => one.method === 'POST').pop()!.body,
                         { locationId: 'DE*GEF*P1', evseUid: 'DE*GEF*E1*1', handOver: 'https://pay.gef.example/handOvers/S1?key=77d0' });

    });

    it('says why the smartphone took none', async () => {

        takeAnswer = refused(409, 'A hand-over is taken from pay.gef.example, the host of the web payment page the proof of the station names: not from evil.example.');

        try
        {

            const root = await open(handOverPage, address('https://evil.example/handOvers/S1'), [ 'charging:read', 'charging:run' ],
                                    smartphone, root => root.querySelector('button.take-over') !== null);

            root.querySelector<HTMLButtonElement>('button.take-over')!.click();

            await until(() => root.querySelector('.form-error') !== null, 'no word of why it was not taken');

            assert.match(root.querySelector('.form-error')!.textContent!, /not from evil\.example/);
            assert.equal(root.querySelector<HTMLButtonElement>('button.take-over')!.disabled, false);

        }
        finally
        {
            takeAnswer = undefined;
        }

    });

    it('takes none of an address that names no charge point, no hand-over, or one that is no https URL', async () => {

        for (const [ path, why ] of [ [ address(null),                                  /names no hand-over/    ],
                                      [ address('http://pay.gef.example/handOvers/S1'), /is no https URL/       ],
                                      [ address('https://pay.gef.example/h/S1', ''),    /names no charge point/ ] ] as const)
        {

            const root = await open(handOverPage, path, [ 'charging:read', 'charging:run' ],
                                    smartphone, root => root.querySelector('.error-box') !== null);

            assert.match(root.querySelector('.error-box')!.textContent!, why);
            assert.equal(root.querySelector('button.take-over'), null);
            assert.ok(root.querySelector('a[href$="/map"]'));
            assert.ok(!asked.some(one => one.path === '/map'), 'the map was asked for nothing to show');

        }

    });

    it('offers no taking over to somebody who may not charge', async () => {

        const root = await open(handOverPage, address('https://pay.gef.example/handOvers/S1'), [ 'charging:read' ],
                                smartphone, root => root.querySelector('.notice') !== null);

        assert.equal(root.querySelector('button.take-over'), null);

    });

});
