#!/usr/bin/env node

const childProcess = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const isWindows = process.platform === 'win32';
const dotnetExe = isWindows ? 'dotnet.exe' : 'dotnet';
const args = process.argv.slice(2);
const exportGithubEnv = args.includes('--export-github-env');
const nxArgs = args.filter((arg) => arg !== '--export-github-env');

function fail(message, result) {
  console.error(message);

  if (result) {
    const stdout = result.stdout?.toString().trim();
    const stderr = result.stderr?.toString().trim();

    if (stdout) {
      console.error(stdout);
    }

    if (stderr) {
      console.error(stderr);
    }
  }

  process.exit(result?.status ?? 1);
}

function findExecutable(name) {
  const pathValue = process.env.PATH ?? '';
  for (const directory of pathValue.split(path.delimiter)) {
    if (!directory) {
      continue;
    }

    const candidate = path.join(directory, name);
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return undefined;
}

function getDotnetRootEnvironmentCandidates() {
  const currentArchitecture = process.arch === 'ia32' ? 'X86' : process.arch.toUpperCase();
  const names = [
    'DOTNET_ROOT',
    `DOTNET_ROOT_${currentArchitecture}`,
    'DOTNET_ROOT_X64',
    'DOTNET_ROOT_X86',
    'DOTNET_ROOT_ARM64',
    'DOTNET_ROOT(x86)',
  ];

  return names
    .map((name) => process.env[name])
    .filter((value, index, values) => value && values.indexOf(value) === index)
    .map((root) => path.join(root, dotnetExe));
}

function getWindowsDotnetCandidates() {
  if (!isWindows) {
    return [];
  }

  const candidates = [
    process.env.ProgramFiles ? path.join(process.env.ProgramFiles, 'dotnet', dotnetExe) : undefined,
    process.env['ProgramFiles(x86)'] ? path.join(process.env['ProgramFiles(x86)'], 'dotnet', dotnetExe) : undefined,
    process.env.LocalAppData ? path.join(process.env.LocalAppData, 'Microsoft', 'dotnet', dotnetExe) : undefined,
  ];

  const systemDrive = process.env.SystemDrive ?? 'C:';
  candidates.push(
    path.join(systemDrive, 'Program Files', 'dotnet', dotnetExe),
    path.join(systemDrive, 'Program Files (x86)', 'dotnet', dotnetExe),
  );

  return candidates;
}

function findDotnet() {
  const candidates = [
    ...getDotnetRootEnvironmentCandidates(),
    path.join(repoRoot, '.dotnet', dotnetExe),
    os.homedir() ? path.join(os.homedir(), '.dotnet', dotnetExe) : undefined,
    ...getWindowsDotnetCandidates(),
    '/usr/local/share/dotnet/dotnet',
    '/opt/homebrew/share/dotnet/dotnet',
    findExecutable(dotnetExe),
  ];

  for (const candidate of candidates) {
    if (candidate && fs.existsSync(candidate)) {
      return path.resolve(candidate);
    }
  }

  fail('Could not find dotnet. Install the .NET SDK or set PATH so dotnet is available.');
}

function run(dotnetPath, args, options = {}) {
  return childProcess.spawnSync(dotnetPath, args, {
    encoding: 'utf8',
    windowsHide: true,
    ...options,
  });
}

function getDotnetOutput(dotnetPath, args, description) {
  const result = run(dotnetPath, args);
  if (result.status !== 0) {
    fail(`Failed to ${description}.`, result);
  }

  return result.stdout.trim();
}

function getSdkDirectory(dotnetPath, sdkVersion) {
  const sdkLines = getDotnetOutput(dotnetPath, ['--list-sdks'], 'list installed .NET SDKs').split(/\r?\n/);
  const escapedVersion = sdkVersion.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const sdkLineRegex = new RegExp(`^${escapedVersion}\\s+\\[(.+)\\]`);

  for (const line of sdkLines) {
    const match = sdkLineRegex.exec(line);
    if (match) {
      return path.join(match[1], sdkVersion);
    }
  }

  fail(`Could not find SDK directory for .NET SDK ${sdkVersion}.`);
}

const dotnetPath = findDotnet();
const dotnetRoot = path.dirname(dotnetPath);
const sdkVersion = getDotnetOutput(dotnetPath, ['--version'], 'determine the selected .NET SDK version');
const sdkDirectory = getSdkDirectory(dotnetPath, sdkVersion);
const sdkResolversDirectory = path.join(sdkDirectory, 'SdkResolvers');
const sdksDirectory = path.join(sdkDirectory, 'Sdks');

if (!fs.existsSync(sdkResolversDirectory)) {
  fail(`Could not find SDK resolvers directory: ${sdkResolversDirectory}`);
}

if (!fs.existsSync(sdksDirectory)) {
  fail(`Could not find SDKs directory: ${sdksDirectory}`);
}

function appendGithubEnvironment(resolverEnvironment) {
  const githubEnvironmentFile = process.env.GITHUB_ENV;
  if (!githubEnvironmentFile) {
    fail('GITHUB_ENV is not set. The --export-github-env option must run inside GitHub Actions.');
  }

  const githubPathFile = process.env.GITHUB_PATH;
  const writePathToEnvironment = !githubPathFile;
  if (githubPathFile) {
    fs.appendFileSync(githubPathFile, `${dotnetRoot}${os.EOL}`);
  } else {
    resolverEnvironment.PATH = `${dotnetRoot}${path.delimiter}${process.env.PATH ?? ''}`;
  }

  const lines = Object.entries(resolverEnvironment)
    .filter(([key]) => writePathToEnvironment || key !== 'PATH')
    .map(([key, value]) => `${key}=${value}`);

  fs.appendFileSync(githubEnvironmentFile, `${lines.join(os.EOL)}${os.EOL}`);
}

const resolverEnv = {
  PATH: `${dotnetRoot}${path.delimiter}${process.env.PATH ?? ''}`,
  DOTNET_ROOT: dotnetRoot,
  DOTNET_ROLL_FORWARD_TO_PRERELEASE: '1',
  DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR: dotnetRoot,
  DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR: sdksDirectory,
  DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER: sdkVersion,
  MSBUILDADDITIONALSDKRESOLVERSFOLDER: sdkResolversDirectory,
  MSBuildSDKsPath: sdksDirectory,
  NX_DAEMON: 'false',
};

if (exportGithubEnv) {
  appendGithubEnvironment({ ...resolverEnv });
  process.exit(0);
}

const env = {
  ...process.env,
  ...resolverEnv,
};

const nxWrapper = path.join(repoRoot, '.nx', 'nxw.js');
if (!fs.existsSync(nxWrapper)) {
  fail('Could not find the Nx wrapper at .nx/nxw.js. Run `npx nx init --useDotNxInstallation --plugins=@nx/dotnet --nxCloud=false` first.');
}

const result = childProcess.spawnSync(process.execPath, [nxWrapper, ...nxArgs], {
  env,
  stdio: 'inherit',
  windowsHide: true,
});

process.exit(result.status ?? 1);
