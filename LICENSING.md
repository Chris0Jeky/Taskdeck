# Taskdeck licensing policy

Last Updated: 2026-08-25

Taskdeck's open-source core is distributed under the GNU General Public
License version 3 only (`GPL-3.0-only`). The complete terms are in
[LICENSE](LICENSE). This policy implements the maintainer decision recorded in
[ADR-0050](docs/decisions/ADR-0050-gplv3-copyleft-core.md).

## Effective date and prior releases

The GPL-3.0-only policy applies to the repository state released on and after
12 August 2026 and to new contributions accepted after that date.

Versions and copies previously released under the MIT License keep the rights
already granted to their recipients. Those grants are not withdrawn. The
former MIT text and copyright notice are retained in
[LICENSES/MIT.txt](LICENSES/MIT.txt) for attribution and licence-compliance
purposes. Their retention does not offer the current Taskdeck project as a
whole under MIT as an alternative to GPL-3.0-only.

## Copyleft core

Modified distributions of the GPL-covered Taskdeck core must remain under
GPLv3 and provide corresponding source as the licence requires. **External
code contributions are currently paused** (maintainer decision effective
2026-08-24; see the notice in
[CONTRIBUTING.md](CONTRIBUTING.md) and issue `#2012`); while they were
accepted, contributions to the core came in under the same GPL-3.0-only terms
(inbound equals outbound), and any future reopening will first state the
inbound-rights instrument (DCO alone does not preserve relicensing
flexibility). Taskdeck uses the Developer Certificate of Origin rather than a
contributor licence agreement; its enforcement is paused (see
[CONTRIBUTING.md](CONTRIBUTING.md)).

Third-party components, assets, and code originally received under compatible
permissive licences retain their own copyright notices and licence terms.

## Separately licensed modules

Future commercial capabilities, if any, may be additive and separately
licensed. They may live under the reserved [`ee/`](ee/) path or in a separate
repository, and each such module must carry explicit licence terms before
distribution.

The `ee/` directory currently contains only a licence placeholder. It contains
no commercial product code and is outside the root GPL grant.

## Free boundary

The following capabilities remain part of Taskdeck's free and open-source
boundary:

- the core capture -> proposal -> review -> apply loop;
- data export and portability;
- bring-your-own API key and local-LLM use; and
- single-user self-hosting.

Capabilities already shipped free, including MFA, OIDC, and board sharing,
also remain in the open-source core. A future paid offering may add separately
licensed modules or managed services, but it will not remove those capabilities
from the GPL-covered core.

## Name and logo

The software licence does not grant permission to use the Taskdeck name, logo,
or other brand identifiers as trademarks, or to imply endorsement by the
Taskdeck project. Descriptive references remain permitted as applicable law
allows.

No trademark registration or availability claim is made in this document.
