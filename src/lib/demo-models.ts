import type { CodexModel } from '../types'

const effort = (reasoningEffort: string, description: string) => ({
  reasoningEffort,
  description,
})

export const demoModels: CodexModel[] = [
  {
    id: 'gpt-5.6-sol',
    model: 'gpt-5.6-sol',
    displayName: 'GPT 5.6 Sol',
    description: 'Balanced default for most coding work.',
    hidden: false,
    supportedReasoningEfforts: [
      effort('light', 'Fast, economical reasoning.'),
      effort('medium', 'Balanced reasoning for everyday work.'),
      effort('high', 'Deeper analysis for complex tasks.'),
      effort('xhigh', 'Extended reasoning for difficult problems.'),
      effort('ultra', 'Maximum reasoning with proactive sub-agents.'),
    ],
    defaultReasoningEffort: 'medium',
    inputModalities: ['text', 'image'],
    isDefault: true,
  },
  {
    id: 'gpt-5.6-terra',
    model: 'gpt-5.6-terra',
    displayName: 'Terra',
    description: 'Efficient model for iterative development.',
    hidden: false,
    supportedReasoningEfforts: [
      effort('light', 'Fast, economical reasoning.'),
      effort('medium', 'Balanced reasoning for everyday work.'),
      effort('high', 'Deeper analysis for complex tasks.'),
      effort('xhigh', 'Extended reasoning for difficult problems.'),
      effort('ultra', 'Maximum reasoning with proactive sub-agents.'),
    ],
    defaultReasoningEffort: 'medium',
    inputModalities: ['text', 'image'],
    isDefault: false,
  },
  {
    id: 'gpt-5.6-luna',
    model: 'gpt-5.6-luna',
    displayName: 'Luna',
    description: 'Lightweight model for fast interactive changes.',
    hidden: false,
    supportedReasoningEfforts: [
      effort('light', 'Fast, economical reasoning.'),
      effort('medium', 'Balanced reasoning for everyday work.'),
      effort('high', 'Deeper analysis for complex tasks.'),
      effort('xhigh', 'Extended reasoning for difficult problems.'),
      effort('ultra', 'Maximum reasoning with proactive sub-agents.'),
    ],
    defaultReasoningEffort: 'medium',
    inputModalities: ['text'],
    isDefault: false,
  },
]
