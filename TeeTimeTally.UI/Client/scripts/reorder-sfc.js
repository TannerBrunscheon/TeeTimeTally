#!/usr/bin/env node
const fs = require('fs').promises;
const path = require('path');

const root = path.resolve(__dirname, '..'); // Client folder

function escapeForRegex(s){ return s.replace(/[-/\\^$*+?.()|[\]{}]/g,'\\$&'); }

async function walk(dir){
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const files = [];
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) files.push(...await walk(full));
    else if (e.isFile() && full.endsWith('.vue')) files.push(full);
  }
  return files;
}

async function processFile(file){
  const txt = await fs.readFile(file, 'utf8');

  // Find blocks
  const scriptSetupRegex = /<script\s+setup[^>]*>[\s\S]*?<\/script>/gi;
  const scriptRegex = /<script(?![^>]*setup)[^>]*>[\s\S]*?<\/script>/gi;
  const templateRegex = /<template[^>]*>[\s\S]*?<\/template>/gi;
  const styleRegex = /<style[^>]*>[\s\S]*?<\/style>/gi;

  const scriptSetupMatches = txt.match(scriptSetupRegex) || [];
  const scriptMatches = txt.match(scriptRegex) || [];
  const templateMatches = txt.match(templateRegex) || [];
  const styleMatches = txt.match(styleRegex) || [];

  const otherScriptCount = scriptMatches.length; // non-setup scripts

  const inventory = {
    file,
    scriptSetupCount: scriptSetupMatches.length,
    scriptCount: otherScriptCount,
    templateCount: templateMatches.length,
    styleCount: styleMatches.length,
  };

  // Conditions to safely reorder:
  // - exactly 1 script setup
  // - 0 other script blocks
  // - exactly 1 template
  // - styleCount <= 1
  if (inventory.scriptSetupCount === 1 && inventory.scriptCount === 0 && inventory.templateCount === 1 && inventory.styleCount <= 1) {
    // Extract the blocks
    const script = scriptSetupMatches[0];
    const template = templateMatches[0];
    const style = styleMatches[0] || '';

    // Any text outside these blocks we should preserve as leading/trailing (rare)
    // Build a new content with canonical order: script, template, style
    const newContent = [script, '\n\n', template, style ? '\n\n' + style : ''].join('');

    if (newContent.trim() !== txt.trim()) {
      // Backup original
      await fs.copyFile(file, file + '.orig');
      await fs.writeFile(file, newContent, 'utf8');
      return { status: 'changed', file };
    }
    return { status: 'unchanged', file };
  }

  // Ambiguous or non-conforming file -> skip
  return { status: 'skipped', file, inventory };
}

(async function main(){
  try{
    const src = path.join(root, 'src');
    const vueFiles = await walk(src);
    const results = { changed: [], skipped: [], unchanged: [], errors: [] };

    for (const f of vueFiles) {
      try{
        const res = await processFile(f);
        results[res.status].push(res.file);
        if (res.status === 'skipped') {
          // write a small per-file inventory for skipped ones
          const invPath = f + '.inventory.json';
          await fs.writeFile(invPath, JSON.stringify(res.inventory, null, 2), 'utf8');
        }
      } catch(err){
        results.errors.push({ file: f, error: String(err) });
      }
    }

    const outPath = path.join(root, 'sfc-reorder-results.json');
    await fs.writeFile(outPath, JSON.stringify(results, null, 2), 'utf8');
    console.log('Done. Results written to', outPath);
    console.log('Changed:', results.changed.length, 'Skipped:', results.skipped.length, 'Unchanged:', results.unchanged.length);
    if (results.errors.length) console.error('Errors:', results.errors);
  } catch(e) {
    console.error('Fatal error', e);
    process.exit(2);
  }
})();
