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

function smartphone({ method, path, body }: Asked): unknown {

    if (path !== '/configuration/map')
        return undefined;

    if (method === 'PUT') {

        const asked = (body as { stationsURL: string }).stationsURL;

        if (asked.length > 0 && !asked.startsWith('http'))
            return refused(400, `'${asked}' is no absolute URL.`);

        stationsURL = asked;

    }

    return { stationsURL, source: 'demo', maxURLLength: 2048, file: '/somewhere/configuration.json' };

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
        assert.match(root.querySelector('.notice')!.textContent!, /but not change it/);

    });

});
