/**
 * Where the map takes its stations from, drawn in a document of happy-dom
 * against a stand-in smartphone: the URL it has, saved as typed, a refusal
 * said beside the form, and no Save for whoever may not change it.
 */

import { asked, field, leave, open, refused, submit, type, until, type Asked } from '../../test/smartphone.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { mapSettingsPage } = await import('./mapSettings.ts');

after(() => leave());


let stationsURL = '';
let trustAnchor = '';

/** The trust anchor of the roaming network QA, as Configuration → Map shows it once it is set. */
const anchorOfQA = {
    roamingNetworkId:  'QA',
    name:              'Quality Assurance',
    description:       null,
    urls:              [ 'https://hub.example/poi/RNs/QA', 'https://mirror.example/poi/RNs/QA' ],
    holders:           [ [ 'e34a4e985a597a60875792c24e98a27a087da4ae8b818f3f45f640142cb770f1' ] ],
    minConsensus:      1,
    uri:               'https://hub.example/poi/RNs/QA?keys=e34a4e98',
    qrCode:            'WWCP/TA-2:8ZKJ683VCBCACVCC',
    json:              {}
};

function smartphone({ method, path, body }: Asked): unknown {

    if (path === '/configuration/roamingNetwork') {

        if (method === 'PUT') {

            const asked = (body as { trustAnchor: string }).trustAnchor;

            if (asked.length > 0 && !asked.startsWith('WWCP/TA-2:'))
                return refused(400, 'That is no trust anchor: A trust anchor is the text of its QR code, or its URL!');

            trustAnchor = asked;

        }

        return { set: trustAnchor.length > 0, trustAnchor: trustAnchor.length > 0 ? anchorOfQA : null, maxTextLength: 8192, file: '/somewhere/configuration.json' };

    }

    if (path !== '/configuration/map')
        return undefined;

    if (method === 'PUT') {

        const asked = (body as { stationsURL: string }).stationsURL;

        if (asked.length > 0 && !asked.startsWith('http'))
            return refused(400, `'${asked}' is no absolute URL.`);

        stationsURL = asked;

    }

    return { stationsURL, source: trustAnchor.length > 0 ? 'roamingNetwork' : 'demo', maxURLLength: 2048, file: '/somewhere/configuration.json' };

}


describe('the map settings', () => {

    it('show the URL the smartphone has, empty by default, and save what is typed', async () => {

        stationsURL = '';

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read', 'map:edit' ],
                                smartphone, root => root.querySelector('#map-settings-form') !== null);

        const url = field(root, '#map-settings-form', 'stationsURL');

        assert.equal(url.value, '');
        assert.match(root.textContent!, /not asked yet/);

        type(url, 'https://emsp.example.org/api/v1/locations');
        submit(root, '#map-settings-form');

        await until(() => /Saved/.test(root.querySelector('#map-settings-note')!.textContent!), 'the save was not said');

        assert.deepEqual(asked.find(one => one.method === 'PUT')?.body, { stationsURL: 'https://emsp.example.org/api/v1/locations' });
        assert.equal(field(root, '#map-settings-form', 'stationsURL').value, 'https://emsp.example.org/api/v1/locations');

    });

    it('says why a URL was refused, beside the form', async () => {

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read', 'map:edit' ],
                                smartphone, root => root.querySelector('#map-settings-form') !== null);

        type(field(root, '#map-settings-form', 'stationsURL'), 'emsp.example.org');
        submit(root, '#map-settings-form');

        await until(() => root.querySelector('#map-settings-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#map-settings-error')!.textContent!, /no absolute URL/);

    });

    it('offers no Save to whoever may only look', async () => {

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read' ],
                                smartphone, root => root.querySelector('#map-settings-form') !== null);

        assert.equal(root.querySelector<HTMLButtonElement>('#map-settings-form button[type="submit"]')!.disabled, true);
        assert.equal(root.querySelector<HTMLButtonElement>('#roaming-network-form button[type="submit"]')!.disabled, true);
        assert.match(root.querySelector('.notice')!.textContent!, /but not change it/);

    });

    it('take the trust anchor of a roaming network as pasted, show what it names, and remove it again', async () => {

        trustAnchor = '';

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read', 'map:edit' ],
                                smartphone, root => root.querySelector('#roaming-network-form') !== null);

        assert.match(root.textContent!, /None: the map shows its demo stations/);
        assert.equal(root.querySelector('#roaming-network-remove'), null, 'a Remove with nothing to remove');

        type(field(root, '#roaming-network-form', 'trustAnchor') as unknown as HTMLTextAreaElement, 'WWCP/TA-2:8ZKJ683VCBCACVCC');
        submit(root, '#roaming-network-form');

        await until(() => /Saved: the map shows the stations of Quality Assurance/.test(root.querySelector('#roaming-network-note')!.textContent!), 'the save was not said');

        assert.deepEqual(asked.filter(one => one.method === 'PUT' && one.path === '/configuration/roamingNetwork').pop()?.body, { trustAnchor: 'WWCP/TA-2:8ZKJ683VCBCACVCC' });

        const shown = root.querySelector('.roaming-network-anchor')!.textContent!;

        assert.match(shown, /Quality Assurance \(QA\)/);
        assert.match(shown, /https:\/\/mirror\.example\/poi\/RNs\/QA/);
        assert.match(shown, /e34a4e985a597a60875792c24e98a27a087da4ae8b818f3f45f640142cb770f1/);
        assert.match(shown, /1 of 1 holder/);
        assert.match(root.textContent!, /the stations of its roaming network whatever this says/);

        root.querySelector<HTMLButtonElement>('#roaming-network-remove')!.click();

        await until(() => /Saved: the map shows its demo stations/.test(root.querySelector('#roaming-network-note')!.textContent!), 'the removal was not said');

        assert.deepEqual(asked.filter(one => one.method === 'PUT' && one.path === '/configuration/roamingNetwork').pop()?.body, { trustAnchor: '' });

    });

    it('says why a trust anchor was refused, beside its form', async () => {

        trustAnchor = '';

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read', 'map:edit' ],
                                smartphone, root => root.querySelector('#roaming-network-form') !== null);

        type(field(root, '#roaming-network-form', 'trustAnchor') as unknown as HTMLTextAreaElement, 'a QR code of a parking ticket');
        submit(root, '#roaming-network-form');

        await until(() => root.querySelector('#roaming-network-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#roaming-network-error')!.textContent!, /no trust anchor/);

    });

});
