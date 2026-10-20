const fs = require('fs');
const path = require('path');
const { pinyin } = require('./node_modules/pinyin-pro');
const out = {};
for (let cp = 0x4e00; cp <= 0x9fff; cp++) {
  const ch = String.fromCodePoint(cp);
  try {
    const arr = pinyin(ch, { toneType: 'none', type: 'array', multiple: true });
    if (!arr || !arr.length) continue;
    const first = arr[0];
    if (!first || !/^[a-z]+$/.test(first)) continue;
    const initials = pinyin(ch, { pattern: 'first', toneType: 'none', type: 'array' })[0] || first[0];
    out[ch] = first + '|' + initials;
  } catch (e) {}
}
const target = path.join('src', 'cTools.Core', 'Data', 'pinyin.json');
fs.writeFileSync(target, JSON.stringify(out));
console.log('chars', Object.keys(out).length, 'bytes', fs.statSync(target).size);
