# Catalog Domain Specification

## Purpose

Define as entidades `Book` e `User` do catálogo da biblioteca, suas invariantes de construção e as garantias de unicidade e integridade que o schema de banco precisa fixar antes de qualquer endpoint de empréstimo existir.

## Requirements

### Requirement: Book has required identifying and availability fields
A entidade `Book` SHALL possuir título, ISBN, autor, total de exemplares, exemplares disponíveis, indicador de ativo/inativo e timestamps de criação e atualização. Título, ISBN e autor não podem ser vazios.

#### Scenario: Book created with valid data
- **GIVEN** título não vazio, ISBN não vazio, autor e total de exemplares maior que zero
- **WHEN** um `Book` é construído com `availableCopies` igual a `totalCopies`
- **THEN** o `Book` é criado como ativo, com `availableCopies == totalCopies`

#### Scenario: Book rejects non-positive total copies
- **GIVEN** total de exemplares igual a zero ou negativo
- **WHEN** um `Book` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

#### Scenario: Book rejects available copies greater than total copies
- **GIVEN** exemplares disponíveis maior que o total de exemplares
- **WHEN** um `Book` é construído
- **THEN** a construção falha com um erro de invariante

#### Scenario: Book rejects empty title
- **GIVEN** título vazio ou composto só por espaços
- **WHEN** um `Book` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

#### Scenario: Book rejects empty ISBN
- **GIVEN** ISBN vazio ou composto só por espaços
- **WHEN** um `Book` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

#### Scenario: Book rejects empty author
- **GIVEN** autor vazio ou composto só por espaços
- **WHEN** um `Book` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

### Requirement: Book ISBN is unique after normalization
O ISBN de `Book` SHALL ser único no catálogo. A normalização remove hífens e converte para maiúsculas antes de qualquer comparação ou persistência.

#### Scenario: Duplicate ISBN differing only by hyphens or case is rejected
- **GIVEN** um `Book` já existente com ISBN `978-3-16-148410-0`
- **WHEN** um novo `Book` é criado com ISBN `9783161484100` (mesmo valor normalizado)
- **THEN** a criação é rejeitada por violação de unicidade

#### Scenario: Distinct ISBNs are accepted
- **GIVEN** um `Book` existente com um ISBN normalizado
- **WHEN** um novo `Book` é criado com um ISBN normalizado diferente
- **THEN** a criação é aceita

### Requirement: Book availability is bounded at the database level
O schema SHALL garantir, via constraint de banco independente da lógica de aplicação, que `available_copies` nunca seja negativo nem exceda `total_copies`.

#### Scenario: Direct write violating bounds is rejected by the database
- **GIVEN** um `Book` persistido com `total_copies = 3`
- **WHEN** uma escrita tenta gravar `available_copies = -1` ou `available_copies = 4`
- **THEN** o banco rejeita a escrita por violação da constraint `CHECK`

### Requirement: User has unique email
A entidade `User` SHALL possuir nome e e-mail, com o e-mail único entre todos os usuários. Nome e e-mail não podem ser vazios. O e-mail SHALL ser normalizado para minúsculas antes de qualquer comparação ou persistência, mesma abordagem aplicada ao ISBN de `Book`.

#### Scenario: User created with valid data
- **GIVEN** nome não vazio e e-mail não vazio, sem usuário existente com o mesmo e-mail normalizado
- **WHEN** um `User` é construído
- **THEN** o `User` é criado com sucesso

#### Scenario: User rejects empty name
- **GIVEN** nome vazio ou composto só por espaços
- **WHEN** um `User` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

#### Scenario: User rejects empty email
- **GIVEN** e-mail vazio ou composto só por espaços
- **WHEN** um `User` é construído
- **THEN** a construção falha com um erro de invariante, sem persistir o objeto

#### Scenario: Duplicate email is rejected
- **GIVEN** um `User` já existente com e-mail `leitor@example.com`
- **WHEN** um novo `User` é criado com o mesmo e-mail
- **THEN** a criação é rejeitada por violação de unicidade

#### Scenario: Duplicate email differing only by case is rejected
- **GIVEN** um `User` já existente com e-mail `Leitor@Example.com`
- **WHEN** um novo `User` é criado com e-mail `leitor@example.com` (mesmo valor normalizado)
- **THEN** a criação é rejeitada por violação de unicidade

### Requirement: All temporal columns are UTC timestamptz
Todas as colunas temporais de `Book` e `User` (criação, atualização) SHALL ser do tipo `timestamptz` armazenadas em UTC, obtidas via `TimeProvider` injetado.

#### Scenario: Timestamps are persisted as UTC
- **GIVEN** um `Book` ou `User` sendo criado
- **WHEN** a entidade é persistida
- **THEN** as colunas de timestamp são gravadas como `timestamptz` com valor em UTC
