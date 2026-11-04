param(
    [string]$Profile = 'marathon2026',
    [string]$Region = 'eu-north-1',
    [string]$ExpectedAccountId = '947501357192',
    [string]$CloudFrontDomain = 'd3og61igj28hpj.cloudfront.net',
    [string]$ApiUrl = 'https://3h009qmpe7.execute-api.eu-north-1.amazonaws.com/develop',
    [string]$UserPoolId = 'eu-north-1_AZywMYwZr'
)

$ErrorActionPreference = 'Stop'
$aws = (Get-Command aws -ErrorAction SilentlyContinue)?.Source
if (-not $aws) { $aws = Join-Path $env:LOCALAPPDATA 'Programs\Amazon\AWSCLIV2\aws.exe' }
if (-not (Test-Path -LiteralPath $aws)) { throw 'AWS CLI was not found.' }

function Invoke-Aws([string[]]$Arguments) {
    $result = & $aws @Arguments --profile $Profile --region $Region --output json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "AWS $($Arguments[0..1] -join ' ') failed: $result" }
    return ($result | Out-String).Trim()
}

function AttributeS([string]$value) { return @{ S = $value } }
function AttributeN($value) { return @{ N = [string]$value } }
function AttributeL([string[]]$values) { return @{ L = @($values | ForEach-Object { @{ S = $_ } }) } }

function Write-JsonTemp($value) {
    $path = Join-Path $env:TEMP ("restaurant-seed-$([guid]::NewGuid().ToString('N')).json")
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 12 -Compress), [Text.UTF8Encoding]::new($false))
    return $path
}

function Ensure-Item([string]$table, [string]$id, $item) {
    $keyName = if ($table -eq 'Waiters') { 'userId' } else { 'id' }
    $keyFile = Write-JsonTemp @{ $keyName = (AttributeS $id) }
    try {
        $existing = Invoke-Aws -Arguments @('dynamodb','get-item','--table-name',$table,'--key',"file://$keyFile") | ConvertFrom-Json
    } finally { Remove-Item -LiteralPath $keyFile }
    if ($existing.Item) { Write-Host "[exists] $table/$id"; return }
    $itemFile = Write-JsonTemp $item
    try { $null = Invoke-Aws -Arguments @('dynamodb','put-item','--table-name',$table,'--item',"file://$itemFile",'--condition-expression',"attribute_not_exists($keyName)") }
    finally { Remove-Item -LiteralPath $itemFile }
    Write-Host "[created] $table/$id"
}

$account = (Invoke-Aws -Arguments @('sts','get-caller-identity') | ConvertFrom-Json).Account
if ($account -ne $ExpectedAccountId) { throw "Expected AWS account $ExpectedAccountId, got $account." }
$assetBase = "https://$CloudFrontDomain/assets/img"

$locations = @(
    @{ Id='demo-location-rustaveli'; Address='Rustaveli Avenue, 48'; Image='rustaveli-avenue-48.jpg'; Description='Green & Tasty in the heart of Tbilisi.'; Rating='4.8' },
    @{ Id='demo-location-baratashvili'; Address='Baratashvili Street, 14'; Image='baratashvili-street-14.jpg'; Description='Fresh bowls and seasonal dishes near the old town.'; Rating='4.7' },
    @{ Id='demo-location-abashidze'; Address='Abashidze Street, 9'; Image='abashidze-street-9.jpg'; Description='Plant-forward dining in Vake.'; Rating='4.9' }
)
$dishes = @(
    @{ Id='demo-dish-avocado'; Name='Avocado Pine Nut Bowl'; Image='avocado-pine-nut-bowl.jpg'; Type='1'; Price='18'; Weight='320 g'; Location=$locations[0].Id },
    @{ Id='demo-dish-strawberry'; Name='Fresh Strawberry Mint Salad'; Image='fresh-strawberry-mint-salad.jpg'; Type='0'; Price='15'; Weight='250 g'; Location=$locations[0].Id },
    @{ Id='demo-dish-spring'; Name='Spring Salad'; Image='spring-salad.jpg'; Type='0'; Price='14'; Weight='260 g'; Location=$locations[1].Id },
    @{ Id='demo-dish-sweet-potato'; Name='Roasted Sweet Potato and Lentil Salad'; Image='roasted-sweet-potato-and-lentil-salad.jpg'; Type='1'; Price='19'; Weight='350 g'; Location=$locations[1].Id },
    @{ Id='demo-dish-avocado-vake'; Name='Avocado Pine Nut Bowl Vake'; Image='avocado-pine-nut-bowl.jpg'; Type='1'; Price='18'; Weight='320 g'; Location=$locations[2].Id },
    @{ Id='demo-dish-spring-vake'; Name='Spring Salad Vake'; Image='spring-salad.jpg'; Type='0'; Price='14'; Weight='260 g'; Location=$locations[2].Id }
)

foreach ($location in $locations) {
    $dishIds = @($dishes | Where-Object Location -eq $location.Id | ForEach-Object Id)
    Ensure-Item 'Locations' $location.Id @{
        id = AttributeS $location.Id
        address = AttributeS $location.Address
        description = AttributeS $location.Description
        totalCapacity = AttributeN 6
        averageOccupancy = AttributeS '35%'
        rating = AttributeN $location.Rating
        imageUrl = AttributeS "$assetBase/locations/$($location.Image)"
        specialtyDishIds = AttributeL $dishIds
    }
}

foreach ($dish in $dishes) {
    Ensure-Item 'Dishes' $dish.Id @{
        id = AttributeS $dish.Id
        name = AttributeS $dish.Name
        description = AttributeS "Freshly prepared $($dish.Name.ToLowerInvariant())."
        dishType = AttributeN $dish.Type
        price = AttributeN $dish.Price
        weight = AttributeS $dish.Weight
        calories = AttributeS '320 kcal'
        proteins = AttributeS '12 g'
        fats = AttributeS '14 g'
        carbohydrates = AttributeS '38 g'
        vitamins = AttributeS 'A, C'
        state = AttributeN 1
        locationId = AttributeS $dish.Location
        imageUrl = AttributeS "$assetBase/dishes/$($dish.Image)"
    }
}

foreach ($location in $locations) {
    foreach ($number in 1..2) {
        $tableId = "demo-table-$($location.Id)-$number"
        Ensure-Item 'Tables' $tableId @{
            id = AttributeS $tableId
            locationId = AttributeS $location.Id
            capacity = AttributeN $(if ($number -eq 1) { 2 } else { 4 })
            tableNumber = AttributeN $number
        }
    }
}

$localSecrets = Join-Path $PSScriptRoot '.demo-waiters.local.json'
$saved = if (Test-Path -LiteralPath $localSecrets) { Get-Content -LiteralPath $localSecrets -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$groups = Invoke-Aws -Arguments @('cognito-idp','list-groups','--user-pool-id',$UserPoolId) | ConvertFrom-Json
if (-not ($groups.Groups | Where-Object GroupName -eq 'Waiter')) {
    $null = Invoke-Aws -Arguments @('cognito-idp','create-group','--user-pool-id',$UserPoolId,'--group-name','Waiter')
}

foreach ($location in $locations) {
    $email = "demo-waiter-$($location.Id)-$ExpectedAccountId@example.com"
    if (-not $saved.ContainsKey($location.Id)) {
        $random = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        $saved[$location.Id] = @{ Email = $email; Password = "Demo$([Convert]::ToHexString($random))!a1" }
        [IO.File]::WriteAllText($localSecrets, ($saved | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    }
    $password = $saved[$location.Id].Password
    $user = $null
    try { $user = Invoke-Aws -Arguments @('cognito-idp','admin-get-user','--user-pool-id',$UserPoolId,'--username',$email) | ConvertFrom-Json }
    catch { if ($_ -notmatch 'UserNotFoundException') { throw } }
    if (-not $user) {
        $null = Invoke-Aws -Arguments @('cognito-idp','admin-create-user','--user-pool-id',$UserPoolId,'--username',$email,'--message-action','SUPPRESS','--temporary-password',$password,'--user-attributes',"Name=email,Value=$email",'Name=email_verified,Value=true')
        $user = Invoke-Aws -Arguments @('cognito-idp','admin-get-user','--user-pool-id',$UserPoolId,'--username',$email) | ConvertFrom-Json
    }
    $null = Invoke-Aws -Arguments @('cognito-idp','admin-set-user-password','--user-pool-id',$UserPoolId,'--username',$email,'--password',$password,'--permanent')
    $null = Invoke-Aws -Arguments @('cognito-idp','admin-add-user-to-group','--user-pool-id',$UserPoolId,'--username',$email,'--group-name','Waiter')
    $userId = ($user.UserAttributes | Where-Object Name -eq 'sub' | Select-Object -First 1).Value
    if (-not $userId) { throw "No Cognito sub for $email" }
    Ensure-Item 'Waiters' $userId @{
        userId = AttributeS $userId
        locationId = AttributeS $location.Id
        createdAt = AttributeS ([DateTime]::UtcNow.ToString('O'))
        reservations = AttributeL @()
    }
    Write-Host "[ready] waiter for $($location.Address)"
}

$aqaEnv = Join-Path $PSScriptRoot '..\aqa\restaurant-aqa-framework\.env'
if (-not (Test-Path -LiteralPath $aqaEnv)) {
    $primary = $saved[$locations[0].Id]
    $lines = @(
        "UI_URL=https://$CloudFrontDomain",
        "API_URL=$ApiUrl",
        "WAITER_USERNAME=$($primary.Email)",
        "WAITER_PASSWORD=$($primary.Password)",
        "TESTTABLE_ID=demo-table-$($locations[0].Id)-2",
        'SKIP_PRE_RUN_CLEANUP=true',
        'MAX_PARALLEL_TESTS=2'
    )
    [IO.File]::WriteAllLines($aqaEnv, $lines, [Text.UTF8Encoding]::new($false))
    Write-Host '[ready] local AQA .env'
}

Write-Host 'Demo locations, dishes, tables, and waiters are ready.'
