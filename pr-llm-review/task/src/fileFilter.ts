import micromatch from 'micromatch';
import { ChangedFile } from './models';

const DEFAULT_EXCLUDE_PATTERNS = [
  '**/*.png', '**/*.jpg', '**/*.jpeg', '**/*.gif', '**/*.ico',
  '**/*.pdf', '**/*.zip', '**/*.dll', '**/*.exe', '**/*.bin',
  '**/package-lock.json', '**/yarn.lock', '**/*.lock',
  '**/*.Designer.cs', '**/*.g.cs', '**/*Reference.cs', '**/migrations/*', '**/Migrations/*',
  '**/*.yml', '**/*.yaml', '**/*.json', '**/*.config',
  '**/*.csproj', '**/*.sln', '**/*.vbproj', '**/*.fsproj',
  '**/__snapshots__/*', '**/*.snap',
];

export class FileFilter {
  private readonly excludePatterns: string[];

  constructor(additionalPatterns: string[] = []) {
    this.excludePatterns = [...DEFAULT_EXCLUDE_PATTERNS, ...additionalPatterns];
  }

  filter(files: ChangedFile[], maxFilesPerReview: number): {
    included: ChangedFile[];
    skipped: ChangedFile[];
    excluded: ChangedFile[];
  } {
    const included: ChangedFile[] = [];
    const excluded: ChangedFile[] = [];

    for (const file of files) {
      // Normalise path to forward slashes for micromatch
      const normalisedPath = file.path.replace(/\\/g, '/').replace(/^\//, '');
      const isExcluded = micromatch.isMatch(normalisedPath, this.excludePatterns, { dot: true });
      if (isExcluded) {
        excluded.push(file);
      } else {
        included.push(file);
      }
    }

    const skipped: ChangedFile[] = [];
    const finalIncluded = included.slice(0, maxFilesPerReview);

    if (included.length > maxFilesPerReview) {
      skipped.push(...included.slice(maxFilesPerReview));
    }

    return { included: finalIncluded, skipped, excluded };
  }
}
