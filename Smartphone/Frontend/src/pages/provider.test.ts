/**
 * The account at the e-mobility provider, drawn in a document of happy-dom
 * against a stand-in smartphone: none at first, made by an administrator with
 * what is typed, shown with its account key and ticket issuer, forgotten
 * again - a refusal said beside the form, and no form for a driver.
 */

import { asked, field, leave, open, refused, submit, type, until, type Asked } from '../../test/smartphone.ts';

import type { ProviderSettings } from '../api/client';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { providerPage } = await import('./provider.ts');

after(() => leave());


const none: ProviderSettings = { set: false, account: null, ticketValidity: 60, directory: '/somewhere/provider' };

const phone1: ProviderSettings = {
    set:      true,
    account:  {
        url:           'https://emsp.example.org/',
        username:      'phone1',
        email:         'phone1@example.org',
        madeUp:        true,
        party:         'DE*GDF',
        since:         '2026-10-28T12:00:00Z',
        accountKey:    { id: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90', subject: 'CN=phone1', issuer: 'CN=Account CA',
                         notBefore: '2026-10-28T11:59:00Z', notAfter: '2028-10-28T11:59:00Z' },
        ticketIssuer:  { subject: 'CN=Ticket issuer', fingerprint: '0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0', notAfter: '2031-10-28T00:00:00Z' }
    },
    ticketValidity:  60,
    directory:       '/somewhere/provider'
};

let current = none;

function smartphone({ method, path, body }: Asked): unknown {

    if (method === 'GET' && path === '/provider')
        return current;

    if (method === 'POST' && path === '/provider/signup') {

        const asked = body as { url: string; username: string; email: string };

        if (!asked.url.startsWith('http'))
            return refused(400, `'${asked.url}' is no http or https URL of an e-mobility provider.`);

        current = phone1;
        return { ...phone1, message: 'This phone has the account \'phone1\' at DE*GDF now.' };

    }

    if (method === 'POST' && path === '/provider/forget') {
        current = none;
        return { ...none, message: 'This phone has forgotten its account at DE*GDF.' };
    }

    return undefined;

}


describe('the e-mobility provider', () => {

    it('makes an account with what is typed, shows its keys, and forgets it again', async () => {

        current = none;

        const root = await open(providerPage, '/configuration/provider', [ 'provider:read', 'provider:edit' ],
                                smartphone, root => root.querySelector('#provider-signup-form') !== null);

        type(field(root, '#provider-signup-form', 'url'),      'https://emsp.example.org/');
        type(field(root, '#provider-signup-form', 'username'), 'phone1');
        type(field(root, '#provider-signup-form', 'email'),    'phone1@example.org');
        submit(root, '#provider-signup-form');

        await until(() => root.querySelector('.provider-account') !== null, 'the account was not shown');

        assert.deepEqual(asked.filter(one => one.path === '/provider/signup').pop()?.body,
                         { url: 'https://emsp.example.org/', username: 'phone1', email: 'phone1@example.org' });

        const text = root.textContent!;

        assert.match(text, /DE\*GDF/);
        assert.match(text, /made up by this phone, and kept by it alone/);
        assert.match(text, /a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90/);
        assert.match(text, /CN=Ticket issuer/);
        assert.match(text, /good for\s+60 minutes/);
        assert.match(root.querySelector('#provider-note')!.textContent!, /has the account 'phone1'/);

        root.querySelector<HTMLButtonElement>('#provider-forget')!.click();

        await until(() => root.querySelector('#provider-signup-form') !== null, 'the account was not forgotten');

        assert.match(root.querySelector('#provider-note')!.textContent!, /has forgotten its account/);

    });

    it('says why an account was not made, beside its form', async () => {

        current = none;

        const root = await open(providerPage, '/configuration/provider', [ 'provider:read', 'provider:edit' ],
                                smartphone, root => root.querySelector('#provider-signup-form') !== null);

        type(field(root, '#provider-signup-form', 'url'),      'emsp.example.org');
        type(field(root, '#provider-signup-form', 'username'), 'phone1');
        type(field(root, '#provider-signup-form', 'email'),    'phone1@example.org');
        submit(root, '#provider-signup-form');

        await until(() => root.querySelector('#provider-signup-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#provider-signup-error')!.textContent!, /no http or https URL/);

    });

    it('shows a driver the account, and nothing to change it with', async () => {

        current = phone1;

        const root = await open(providerPage, '/configuration/provider', [ 'provider:read' ],
                                smartphone, root => root.querySelector('.provider-account') !== null);

        assert.equal(root.querySelector('#provider-forget'), null);
        assert.match(root.querySelector('.notice')!.textContent!, /but not make, take or forget one/);

    });

});
