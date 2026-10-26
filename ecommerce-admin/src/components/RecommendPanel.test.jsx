import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { jsonResponse, mockApi, problem, product } from '../test/mockApi'
import RecommendPanel from './RecommendPanel'

const answer = (source, items = []) => ({ query: 'mouse', source, items })
const item = { product: product({ name: 'Wireless Mouse' }), score: 88, reason: 'name matches "mouse".' }

async function ask(query = 'mouse') {
  await userEvent.type(screen.getByLabelText('Shopper query'), query)
  await userEvent.click(screen.getByRole('button', { name: 'Recommend' }))
}

describe('RecommendPanel', () => {
  it.each([
    ['gemini', 'Ranked by Gemini'],
    ['rules', 'Ranked by rules (AI not configured)'],
    ['rules-fallback', 'Ranked by rules (AI unavailable)'],
  ])('labels the %s engine honestly', async (source, label) => {
    mockApi({ 'GET /api/v1/products/recommend': answer(source, [item]) })
    render(<RecommendPanel />)

    await ask()

    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.getByText('Wireless Mouse')).toBeInTheDocument()
    expect(screen.getByText('88')).toBeInTheDocument()
  })

  it('sends the trimmed query and says so when nothing is relevant', async () => {
    const api = mockApi({ 'GET /api/v1/products/recommend': answer('rules', []) })
    render(<RecommendPanel />)

    await ask('  zzz  ')

    expect(await screen.findByText('No relevant products.')).toBeInTheDocument()
    expect(api.calls[0].params.get('query')).toBe('zzz')
  })

  it('tells the user when the rate limit is hit', async () => {
    mockApi({ 'GET /api/v1/products/recommend': jsonResponse(429, problem(429)) })
    render(<RecommendPanel />)

    await ask()

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many searches')
  })
})
