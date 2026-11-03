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


let stationsURL  = '';
let trustAnchor  = '';
let trusted      = false;
let validly      = true;

const keyId = 'e34a4e985a597a60875792c24e98a27a087da4ae8b818f3f45f640142cb770f1';

/** The trust anchor of the roaming network QA, as Configuration → Map shows it once it is set. */
const anchorOfQA = {
    roamingNetworkId:  'QA',
    name:              'Quality Assurance',
    description:       null,
    urls:              [ 'https://hub.example/poi/RNs/QA', 'https://mirror.example/poi/RNs/QA' ],
    holders:           [ [ keyId ] ],
    minConsensus:      1,
    uri:               'https://hub.example/poi/RNs/QA?keys=e34a4e98',
    qrCode:            'WWCP/TA-2:8ZKJ683VCBCACVCC',
    json:              {}
};

/** The roaming network section as the smartphone has it now. */
const roamingNetwork = () => ({
    set:             trustAnchor.length > 0,
    trustAnchor:     trustAnchor.length > 0 ? anchorOfQA : null,
    trustedKeys:     trusted ? [ { id: keyId, acceptedAt: '2026-11-03T09:00:00Z', acceptedBy: 'admin1' } ] : [],
    trustedHolders:  trusted ? 1 : 0,
    quorumTrusted:   trusted,
    maxTextLength:   8192,
    file:            '/somewhere/configuration.json'
});

/** The key statement of OCC, as the network serves it, and how it fares against the key trusted here. */
const keyStatementOfOCC = () => ({
    checkedAt:     '2026-11-03T09:00:00Z',
    from:          'https://hub.example/poi/RNs/QA',
    error:         null,
    keyStatement:  { roamingNetworkId: 'QA', operatorId: 'OCC', signedAt: '2026-11-01T00:00:00Z', validUntil: '2027-11-01T00:00:00Z', minConsensus: 1,
                     signedBy: [ keyId ], identityKeys: [ { id: keyId, holder: 'OCC board', notBefore: null, notAfter: '2028-01-01T00:00:00Z', key: 'pQECAyY=' } ] },
    evaluation:    { signaturesValid: validly, signersTrusted: trusted, quorumReached: validly && trusted, withinValidity: true, canTrustKeys: validly,
                     trustedHolders: trusted ? 1 : 0, requiredConsensus: 1, signatures: 1, error: validly ? null : 'The signature of the key statement is invalid!' }
});

function smartphone({ method, path, body }: Asked): unknown {

    if (path === '/configuration/roamingNetwork') {

        if (method === 'PUT') {

            const asked = (body as { trustAnchor: string }).trustAnchor;

            if (asked.length > 0 && !asked.startsWith('WWCP/TA-2:'))
                return refused(400, 'That is no trust anchor: A trust anchor is the text of its QR code, or its URL!');

            if (asked !== trustAnchor)
                trusted = false;

            trustAnchor = asked;

        }

        return roamingNetwork();

    }

    if (path === '/configuration/roamingNetwork/keyStatement')
        return keyStatementOfOCC();

    if (path === `/configuration/roamingNetwork/trustedKeys/${keyId}`) {
        trusted = method === 'PUT';
        return roamingNetwork();
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
        assert.match(shown, /1 of 1 holder/);
        assert.match(root.querySelector('.trust-keys')!.textContent!, /e34a4e985a597a60875792c24e98a27a087da4ae8b818f3f45f640142cb770f1/);
        assert.match(root.textContent!, /the stations of its roaming network whatever this says/);
        assert.match(root.querySelector('#roaming-network-note')!.textContent!, /none as signed, before its keys are trusted/);

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

    it('trust no key of a trust anchor set, show its key statement, and trust a key once confirmed - and no longer', async () => {

        trustAnchor  = 'WWCP/TA-2:8ZKJ683VCBCACVCC';
        trusted      = false;
        validly      = true;

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read', 'map:edit' ],
                                smartphone, root => /Signed validly/.test(root.textContent!));

        const key = () => root.querySelector<HTMLElement>(`.trust-key[data-key-id="${keyId}"]`)!;

        assert.ok(root.querySelector('.roaming-network-untrusted'), 'it is not said that no station is shown as signed');
        assert.match(key().textContent!, /not trusted/);
        assert.match(key().textContent!, /held by OCC board/);
        assert.match(root.querySelector('.key-statement')!.textContent!, /OCC/);
        assert.match(root.textContent!, /0 of the 1 holder\(s\) required/);

        // Asked first, and nothing trusted before it is confirmed.
        key().querySelector<HTMLButtonElement>('.trust-key-start')!.click();

        await until(() => key().querySelector('.trust-key-confirmation') !== null, 'no confirmation was asked for');

        assert.equal(asked.filter(one => one.path.includes('/trustedKeys/')).length, 0, 'trusted before it was confirmed');
        assert.match(key().querySelector('.trust-key-confirmation')!.textContent!, /Trust it only where you know it is the operator's/);

        key().querySelector<HTMLButtonElement>('.trust-key-confirm')!.click();

        await until(() => /Trusted: keys of 1 holder\(s\) are trusted here\./.test(root.querySelector('#roaming-network-trust-note')!.textContent!), 'the trust was not said');

        assert.equal(asked.filter(one => one.method === 'PUT' && one.path === `/configuration/roamingNetwork/trustedKeys/${keyId}`).length, 1);
        assert.match(key().textContent!, /Trusted since .*, by admin1/);
        assert.equal(root.querySelector('.roaming-network-untrusted'), null, 'still said to be trusted by too few');

        key().querySelector<HTMLButtonElement>('.trust-key-withdraw')!.click();

        await until(() => /No longer trusted/.test(root.querySelector('#roaming-network-trust-note')!.textContent!), 'the withdrawal was not said');

        assert.equal(asked.filter(one => one.method === 'DELETE' && one.path === `/configuration/roamingNetwork/trustedKeys/${keyId}`).length, 1);
        assert.ok(root.querySelector('.roaming-network-untrusted'));

    });

    it('say where a key statement is not signed validly, and offer no trust to whoever may only look', async () => {

        trustAnchor  = 'WWCP/TA-2:8ZKJ683VCBCACVCC';
        trusted      = false;
        validly      = false;

        const root = await open(mapSettingsPage, '/configuration/map', [ 'map:read' ],
                                smartphone, root => /Signed validly/.test(root.textContent!));

        assert.match(root.textContent!, /is not signed validly, or not valid now: what it names is no reason to trust a key/);
        assert.match(root.textContent!, /The signature of the key statement is invalid!/);
        assert.equal(root.querySelector('.trust-key-start'), null, 'a Trust to whoever may only look');

        validly = true;

    });

});
