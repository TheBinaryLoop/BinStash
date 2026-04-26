import gql from 'graphql-tag'
import { normalizeGraphqlError, runQuery } from '../shared/api/graphqlComposable'

export type InstanceUserDto = {
  id: string
  firstName: string
  middleName?: string | null
  lastName: string
  email: string
  isEmailVerified: boolean
  isOnboardingCompleted: boolean
}

function toInstanceUserDto(x: any): InstanceUserDto {
  return {
    id: x.id,
    firstName: x.firstName,
    middleName: x.middleName ?? null,
    lastName: x.lastName,
    email: x.email,
    isEmailVerified: Boolean(x.isEmailVerified),
    isOnboardingCompleted: Boolean(x.isOnboardingCompleted),
  }
}

const LIST_INSTANCE_USERS_QUERY = gql`
  query ListInstanceUsers($first: Int) {
    users(first: $first, order: [{ firstName: ASC }, { lastName: ASC }, { email: ASC }]) {
      nodes {
        id
        firstName
        middleName
        lastName
        email
        isEmailVerified
        isOnboardingCompleted
      }
      totalCount
    }
  }
`

export async function listInstanceUsers(first = 50): Promise<InstanceUserDto[]> {
  try {
    const data = await runQuery<any>(LIST_INSTANCE_USERS_QUERY, { first })
    return (data?.users?.nodes ?? []).map(toInstanceUserDto)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Failed to load users.')
  }
}