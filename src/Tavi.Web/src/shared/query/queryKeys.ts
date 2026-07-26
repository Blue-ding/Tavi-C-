export const queryKeys = {
  world: {
    workspace: ['world', 'workspace'] as const,
    types: ['world', 'types'] as const,
    moduleActions: ['world', 'module-actions'] as const,
  },
  scenario: {
    workspace: (seed: number) => ['scenario', 'workspace', seed] as const,
    worldLink: ['scenario', 'world-link'] as const,
  },
  performance: {
    workspace: (seed: number) => ['performance', 'workspace', seed] as const,
    archives: ['performance', 'archives'] as const,
    archive: (id: string) => ['performance', 'archives', id] as const,
  },
  writing: {
    workspace: ['writing', 'workspace'] as const,
    manuscript: (id: string) => ['writing', 'manuscript', id] as const,
  },
  guidance: {
    availability: ['guidance', 'availability'] as const,
    session: ['guidance', 'session'] as const,
  },
  extensions: {
    workspace: ['extensions', 'workspace'] as const,
  },
  settings: {
    languageModel: ['settings', 'language-model'] as const,
    openAI: ['settings', 'openai'] as const,
  },
} as const;
