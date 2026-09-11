/**
 * Deterministic timezone control for specs (#2943).
 *
 * ## Why not `vi.stubEnv('TZ', …)`
 *
 * `vi.stubEnv('TZ', zone)` writes the string into `import.meta.env`, which
 * Vitest mirrors onto `process.env`. Changing the *runtime's* zone is a side
 * effect of that write: Node's real environment store calls V8's
 * `DateTimeConfigurationChangeNotification` when the key is `TZ`, which makes
 * `Date` and `Intl` re-detect the zone.
 *
 * That side effect does not happen in every Vitest pool. Measured on Node
 * 24.19.0 / Vitest 5.0.0 with `environment: 'happy-dom'`:
 *
 * | pool                | `process.env.TZ` after write | `Intl…resolvedOptions().timeZone` |
 * | ------------------- | ---------------------------- | --------------------------------- |
 * | `forks` (repo default) | `Pacific/Kiritimati`      | `Pacific/Kiritimati`              |
 * | `threads`           | `Pacific/Kiritimati`         | **host zone, unchanged**          |
 *
 * `@stryker-mutator/vitest-runner` (v10 `#getVitestPoolConfig`) forces
 * `pool: 'threads', maxWorkers: 1`, so every zone-sensitive spec silently
 * measures the CI runner's own zone during Stryker's Vitest dry run. On a UTC
 * runner the expected +/-1 day shifts collapse to 0 and the dry run fails before
 * a single mutant is executed — Taskdeck issue #2943, workflow run 34518952589.
 *
 * ## What this module does instead
 *
 * Nothing here mutates process state. Every value is derived from
 * `Intl.DateTimeFormat(…, { timeZone })`, which takes the zone as an explicit
 * argument, so results are identical in every pool, on every host, whatever the
 * host clock says.
 *
 * - {@link zonedParts} / {@link offsetMinutesAt} — read a zone's wall clock for
 *   a real instant.
 * - {@link instantAtZonedWallClock} — the inverse: the UTC instant at which a
 *   given zone shows a given wall clock. Use this instead of
 *   `new Date(y, m, d, …)`, whose local-parts constructor reads the *host* zone.
 * - {@link installTimeZone} — makes code under test observe the zone through the
 *   accessors it actually uses, and returns a restore function.
 *
 * `installTimeZone` deliberately does NOT patch `Date`'s local-parts
 * constructor, `Date.parse` of zone-less strings, or the local `setX` mutators.
 * Build instants with {@link instantAtZonedWallClock} rather than relying on
 * those.
 */

/** Zone-local wall clock: `[year, monthIndex, day, hour, minute, second]`. */
export type WallClock = [number, number, number, number, number, number]

export interface ZonedParts {
  year: number
  /** 0-based, to match `Date.prototype.getMonth`. */
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

// Captured before any test can swap the globals (fake timers replace
// `globalThis.Date`; `installTimeZone` replaces `Intl.DateTimeFormat`).
const NativeDate = Date
const NativeDateTimeFormat = Intl.DateTimeFormat

const formatters = new Map<string, Intl.DateTimeFormat>()

function formatterFor(timeZone: string): Intl.DateTimeFormat {
  let formatter = formatters.get(timeZone)
  if (!formatter) {
    formatter = new NativeDateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
    formatters.set(timeZone, formatter)
  }
  return formatter
}

/** The wall clock `timeZone` shows at `instant`. */
export function zonedParts(instant: Date, timeZone: string): ZonedParts {
  const parts: Record<string, number> = {}
  for (const part of formatterFor(timeZone).formatToParts(instant)) {
    if (part.type !== 'literal') parts[part.type] = Number(part.value)
  }
  // Some ICU builds render midnight as hour 24 even under `hourCycle: 'h23'`.
  if (parts.hour === 24) parts.hour = 0
  return {
    year: parts.year,
    month: parts.month - 1,
    day: parts.day,
    hour: parts.hour,
    minute: parts.minute,
    second: parts.second,
  }
}

function wallClockAsUtcMs(parts: ZonedParts): number {
  return NativeDate.UTC(parts.year, parts.month, parts.day, parts.hour, parts.minute, parts.second)
}

/**
 * `timeZone`'s offset at `instant`, in `Date.prototype.getTimezoneOffset`
 * units: minutes to ADD to local time to reach UTC. UTC+14 is `-840`.
 */
export function offsetMinutesAt(instant: Date, timeZone: string): number {
  const utcSeconds = instant.getTime() - (((instant.getTime() % 1000) + 1000) % 1000)
  return Math.round((utcSeconds - wallClockAsUtcMs(zonedParts(instant, timeZone))) / 60_000)
}

/**
 * The UTC instant at which `timeZone` shows `wall`.
 *
 * Two passes: guess with a zero offset, correct with the offset in force at the
 * guess, then re-check — enough for every real zone, including DST edges, since
 * offsets move by at most a couple of hours. Throws on a wall clock that does
 * not exist in the zone (the skipped hour of a DST spring-forward) rather than
 * returning a silently shifted instant.
 */
export function instantAtZonedWallClock(wall: WallClock, timeZone: string): Date {
  const target: ZonedParts = {
    year: wall[0],
    month: wall[1],
    day: wall[2],
    hour: wall[3],
    minute: wall[4],
    second: wall[5],
  }
  const naiveMs = wallClockAsUtcMs(target)
  let instant = new NativeDate(naiveMs + offsetMinutesAt(new NativeDate(naiveMs), timeZone) * 60_000)
  instant = new NativeDate(naiveMs + offsetMinutesAt(instant, timeZone) * 60_000)

  const reached = zonedParts(instant, timeZone)
  if (wallClockAsUtcMs(reached) !== naiveMs) {
    throw new Error(
      `Wall clock [${wall.join(', ')}] does not exist in ${timeZone} (a DST gap); ` +
        `the closest instant shows [${reached.year}, ${reached.month}, ${reached.day}, ` +
        `${reached.hour}, ${reached.minute}, ${reached.second}].`,
    )
  }
  return instant
}

type LocalGetter =
  | 'getFullYear'
  | 'getMonth'
  | 'getDate'
  | 'getDay'
  | 'getHours'
  | 'getMinutes'
  | 'getSeconds'
  | 'getMilliseconds'

// Each local getter is the matching UTC getter read on the instant shifted by
// the zone's offset, so all of them stay mutually consistent by construction.
const LOCAL_GETTERS: Record<LocalGetter, keyof Date> = {
  getFullYear: 'getUTCFullYear',
  getMonth: 'getUTCMonth',
  getDate: 'getUTCDate',
  getDay: 'getUTCDay',
  getHours: 'getUTCHours',
  getMinutes: 'getUTCMinutes',
  getSeconds: 'getUTCSeconds',
  getMilliseconds: 'getUTCMilliseconds',
}

type LocaleMethod = 'toLocaleString' | 'toLocaleDateString' | 'toLocaleTimeString'

const LOCALE_METHODS: LocaleMethod[] = [
  'toLocaleString',
  'toLocaleDateString',
  'toLocaleTimeString',
]

/**
 * Make code under test observe `timeZone` through `Date`'s local accessors and
 * through `Intl`'s default zone, without touching `process.env`.
 *
 * **Ordering:** call `vi.useFakeTimers()` *before* this, not after.
 * `useFakeTimers` replaces the entire `Intl` global object, which would discard
 * the zone default installed here; installing afterwards wraps the clock-aware
 * `Intl` instead of being thrown away by it.
 *
 * Returns a restore function; call it in `afterEach`. Calling the restore
 * function more than once is a no-op.
 */
export function installTimeZone(timeZone: string): () => void {
  // Fail fast on a typo rather than silently formatting in UTC.
  formatterFor(timeZone)

  const prototype = NativeDate.prototype as unknown as Record<string, unknown>
  const originals = new Map<string, unknown>()

  for (const [local, utc] of Object.entries(LOCAL_GETTERS)) {
    originals.set(local, prototype[local])
    prototype[local] = function (this: Date): number {
      const shifted = new NativeDate(this.getTime() - offsetMinutesAt(this, timeZone) * 60_000)
      return (shifted[utc] as () => number).call(shifted)
    }
  }

  originals.set('getTimezoneOffset', prototype.getTimezoneOffset)
  prototype.getTimezoneOffset = function (this: Date): number {
    return offsetMinutesAt(this, timeZone)
  }

  for (const method of LOCALE_METHODS) {
    const original = NativeDate.prototype[method] as (
      this: Date,
      locales?: Intl.LocalesArgument,
      options?: Intl.DateTimeFormatOptions,
    ) => string
    originals.set(method, original)
    prototype[method] = function (
      this: Date,
      locales?: Intl.LocalesArgument,
      options?: Intl.DateTimeFormatOptions,
    ): string {
      return original.call(this, locales, options?.timeZone ? options : { ...options, timeZone })
    }
  }

  // Default the zone for formatters built without one, which is what makes
  // `Intl.DateTimeFormat().resolvedOptions().timeZone` report `timeZone`.
  // Explicit-zone formatters (this module's own included) are left alone.
  //
  // Wrap whatever is installed right now rather than the module-load original:
  // `vi.useFakeTimers()` swaps the whole `Intl` global for a clock-aware stand-in
  // (vitest bundles @sinonjs/fake-timers' `IntlWithClock`), and wrapping that
  // keeps its behaviour instead of discarding it. The flip side is an ordering
  // rule — see this function's doc comment.
  const currentIntl = Intl
  const CurrentDateTimeFormat = currentIntl.DateTimeFormat
  function ZonedDateTimeFormat(
    locales?: Intl.LocalesArgument,
    options?: Intl.DateTimeFormatOptions,
  ): Intl.DateTimeFormat {
    return new CurrentDateTimeFormat(
      locales,
      options?.timeZone ? options : { ...options, timeZone },
    )
  }
  ZonedDateTimeFormat.prototype = CurrentDateTimeFormat.prototype
  ZonedDateTimeFormat.supportedLocalesOf =
    CurrentDateTimeFormat.supportedLocalesOf.bind(CurrentDateTimeFormat)
  currentIntl.DateTimeFormat = ZonedDateTimeFormat as unknown as typeof Intl.DateTimeFormat

  let restored = false
  return () => {
    if (restored) return
    restored = true
    for (const [name, original] of originals) prototype[name] = original
    currentIntl.DateTimeFormat = CurrentDateTimeFormat
  }
}
