// Minimal, dependency-free glob matcher for repository-relative paths.
// Semantics (deliberately small and explicit — the policy is written against them):
//   **      matches any sequence of characters, including '/'
//   **/     as a prefix or in the middle matches zero or more directories
//   *       matches any sequence of non-'/' characters
//   ?       matches one non-'/' character
//   everything else is literal; patterns match the FULL path from the repository root.
// A pattern with no '/' therefore only matches root-level files — write `**/*.md`
// when any depth is intended. This keeps the policy unambiguous and testable.

const cache = new Map();

export function globToRegExp(pattern) {
  if (cache.has(pattern)) return cache.get(pattern);
  let source = '^';
  for (let index = 0; index < pattern.length; index += 1) {
    const char = pattern[index];
    if (char === '*') {
      if (pattern[index + 1] === '*') {
        const followedBySlash = pattern[index + 2] === '/';
        source += followedBySlash ? '(?:.*/)?' : '.*';
        index += followedBySlash ? 2 : 1;
      } else {
        source += '[^/]*';
      }
    } else if (char === '?') {
      source += '[^/]';
    } else if ('\\^$.|+()[]{}'.includes(char)) {
      source += `\\${char}`;
    } else {
      source += char;
    }
  }
  source += '$';
  const regExp = new RegExp(source);
  cache.set(pattern, regExp);
  return regExp;
}

export function matchesGlob(path, pattern) {
  return globToRegExp(pattern).test(path);
}

export function matchesAny(path, patterns) {
  return patterns.some((pattern) => matchesGlob(path, pattern));
}
