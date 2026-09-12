export const PSEUDO_LANGUAGE = 'en-XA';

const accentedCharacters = Object.freeze({
  A: 'Å', B: 'Ɓ', C: 'Ç', D: 'Ð', E: 'Ë', F: 'Ƒ', G: 'Ĝ', H: 'Ħ', I: 'Ï',
  J: 'Ĵ', K: 'Ķ', L: 'Ļ', M: 'Ḿ', N: 'Ñ', O: 'Ö', P: 'Þ', Q: 'Ǫ', R: 'Ŗ',
  S: 'Š', T: 'Ţ', U: 'Ü', V: 'Ṽ', W: 'Ŵ', X: 'Ẍ', Y: 'Ÿ', Z: 'Ž',
  a: 'å', b: 'ƀ', c: 'ç', d: 'ð', e: 'ë', f: 'ƒ', g: 'ĝ', h: 'ħ', i: 'ï',
  j: 'ĵ', k: 'ķ', l: 'ļ', m: 'ḿ', n: 'ñ', o: 'ö', p: 'þ', q: 'ǫ', r: 'ŗ',
  s: 'š', t: 'ţ', u: 'ü', v: 'ṽ', w: 'ŵ', x: 'ẍ', y: 'ÿ', z: 'ž',
});

const protectedToken = /(\{\{[\s\S]*?\}\}|<\/?[A-Za-z][^<>]*>)/g;

function pseudoLiteral(segment) {
  const leading = segment.match(/^\s*/u)?.[0] ?? '';
  const trailing = segment.match(/\s*$/u)?.[0] ?? '';
  const end = segment.length - trailing.length;
  const literal = segment.slice(leading.length, end);
  if (literal === '') return segment;

  const accented = [...literal].map((character) => accentedCharacters[character] ?? character).join('');
  const expandedLength = Math.ceil(literal.length * 1.35);
  const padding = '~'.repeat(Math.max(0, expandedLength - accented.length - 2));
  return `${leading}［${accented}${padding}］${trailing}`;
}

export function pseudoLocalizeText(value) {
  return value.split(protectedToken).map((segment, index) => (
    index % 2 === 1 ? segment : pseudoLiteral(segment)
  )).join('');
}

export function createPseudoResources(value) {
  if (typeof value === 'string') return pseudoLocalizeText(value);
  if (Array.isArray(value)) return value.map(createPseudoResources);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, child]) => [key, createPseudoResources(child)]),
    );
  }
  return value;
}

export function resolvePseudoLanguage({ isDevelopment, search }) {
  if (!isDevelopment) return null;
  return new URLSearchParams(search).get('lng') === PSEUDO_LANGUAGE ? PSEUDO_LANGUAGE : null;
}
