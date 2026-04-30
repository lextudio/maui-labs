// @ts-check
//
// Nx createNodesV2 plugin that augments project metadata with `os:<name>` tags
// derived from each project's `<NxBuildableOn>` MSBuild property. Lets CI
// filter affected work by runner OS without a centralized exclusion list:
//
//   ./nx affected -t build --projects=tag:os:macos,tag:os:any
//
// Resolution order (first match wins):
//   1. The csproj/fsproj/vbproj itself contains <NxBuildableOn>...</NxBuildableOn>.
//   2. Walk up the directory tree from the project, checking each
//      Directory.Build.props for <NxBuildableOn>...</NxBuildableOn>.
//   3. Default: linux;macos;windows  (i.e. "any host can do a managed build").
//
// Format: a semicolon- or comma-separated list of `linux`, `macos`, `windows`.
// Each value becomes a `os:<value>` tag. We also always emit `os:any` when
// all three are present, so workflow matrices can use `tag:os:<runner>,tag:os:any`
// to pick up "buildable anywhere" projects on every runner.
//
// LIMITATION: this is a regex over the raw XML. It does NOT evaluate MSBuild
// conditions. If you need a value to depend on a property like $(TargetFramework)
// or $(Configuration), set NxBuildableOn directly in the csproj (where you
// already have the context-specific property like <DevFlowSamplePlatform>).

const fs = require('fs');
const path = require('path');

const ALL_OSES = ['linux', 'macos', 'windows'];
const NX_BUILDABLE_ON_REGEX = /<NxBuildableOn[^>]*>([^<]+)<\/NxBuildableOn>/i;
const fileCache = new Map();

function readBuildableOn(filePath) {
  if (fileCache.has(filePath)) return fileCache.get(filePath);
  let value = null;
  try {
    const text = fs.readFileSync(filePath, 'utf8');
    const m = text.match(NX_BUILDABLE_ON_REGEX);
    if (m) value = m[1].trim();
  } catch {
    // ignore unreadable files
  }
  fileCache.set(filePath, value);
  return value;
}

function findBuildableOn(projectFileAbs, workspaceRoot) {
  const direct = readBuildableOn(projectFileAbs);
  if (direct) return direct;

  let dir = path.dirname(projectFileAbs);
  const root = path.resolve(workspaceRoot);
  // Hard cap at 16 levels in case of path weirdness.
  for (let i = 0; i < 16; i++) {
    const candidate = path.join(dir, 'Directory.Build.props');
    if (fs.existsSync(candidate)) {
      const value = readBuildableOn(candidate);
      if (value) return value;
    }
    if (dir === root || dir === path.dirname(dir)) break;
    dir = path.dirname(dir);
  }
  return null;
}

function parseOsList(value) {
  return Array.from(
    new Set(
      value
        .split(/[;,\s]+/)
        .map((s) => s.trim().toLowerCase())
        .filter(Boolean)
    )
  );
}

module.exports = {
  createNodesV2: [
    '**/*.{csproj,fsproj,vbproj}',
    async (projectFiles, _options, context) => {
      return projectFiles.map((relPath) => {
        const abs = path.join(context.workspaceRoot, relPath);
        const raw = findBuildableOn(abs, context.workspaceRoot) ?? ALL_OSES.join(';');
        const oses = parseOsList(raw).filter((o) => ALL_OSES.includes(o));
        if (oses.length === 0) oses.push(...ALL_OSES);

        const tags = oses.map((o) => `os:${o}`);
        if (oses.length === ALL_OSES.length) tags.push('os:any');

        return [
          relPath,
          {
            projects: {
              [path.dirname(relPath)]: {
                tags,
                metadata: {
                  nxBuildableOn: oses,
                },
              },
            },
          },
        ];
      });
    },
  ],
};
