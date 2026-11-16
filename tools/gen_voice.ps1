# Generates the APL voice library with Amazon Polly (generative engine,
# us-east-1): Arjun = auctioneer, Kajal = assistant. Re-runnable: only lines
# whose text changed (or are new) are synthesized again.
#
#   powershell -File tools\gen_voice.ps1
#
# Output: audio/voice/<voice>/<key>.mp3 + audio/voice/voice_lines.json
# (key -> voice, text, file) for the Unity client.
param([string]$Profile = 'default')  # AWS CLI profile used for Amazon Polly
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
$utf8 = New-Object System.Text.UTF8Encoding $false
$data = [IO.File]::ReadAllText((Join-Path $repo 'data\apl_dataset.json'), $utf8) | ConvertFrom-Json
$outDir = Join-Path $repo 'audio\voice'

$ones = 'zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen'.Split(' ')
$tens = ',,twenty,thirty,forty,fifty,sixty,seventy,eighty,ninety'.Split(',')
function Words([int]$n) {
  if ($n -lt 20) { return $ones[$n] }
  if ($n -lt 100) { $t = $tens[[math]::Floor($n / 10)]; if ($n % 10) { return "$t $($ones[$n % 10])" } else { return $t } }
  $h = "$($ones[[math]::Floor($n / 100)]) hundred"
  if ($n % 100) { return "$h and $(Words ($n % 100))" } else { return $h }
}
function AmountWords([int]$lakhs) {
  $cr = [math]::Floor($lakhs / 100); $l = $lakhs % 100
  $parts = @()
  if ($cr -gt 0) { $parts += "$(Words $cr) crore" }
  if ($l -gt 0) { $parts += "$(Words $l) lakh" }
  return ($parts -join ' ')
}

$lines = [ordered]@{}
function Add([string]$voice, [string]$key, [string]$text) { $script:lines["$voice/$key"] = @{ voice = $voice; key = $key; text = $text } }

# --- Auctioneer: calls
Add arjun welcome 'Ladies and gentlemen, welcome to the A P L auction!'
Add arjun set_marquee "Let's begin with the marquee players."
Add arjun set_batters 'Next up, the batters.'
Add arjun set_bowlers 'Now, the bowlers.'
Add arjun set_allRounders 'Up next, the all-rounders.'
Add arjun set_wicketKeepers 'Now, the wicketkeepers.'
Add arjun set_uncapped 'And now, the uncapped players.'
Add arjun set_accelerated 'We now move to the accelerated round.'
Add arjun role_BAT 'a batter'
Add arjun role_BOWL 'a bowler'
Add arjun role_AR 'an all-rounder'
Add arjun role_WK 'a wicketkeeper'
foreach ($c in ($data.players | ForEach-Object { $_.country } | Sort-Object -Unique)) {
  Add arjun ("from_" + ($c -replace ' ', '_')) "from $c"
}
Add arjun base_price 'Base price,'
Add arjun open 'Do I hear a bid?'
Add arjun any_more 'Do I hear more?'
Add arjun with_team 'The bid is with the'
# Said when the room goes quiet, before "going once".
Add arjun any_1 'Anyone else?'
Add arjun any_2 'Any more bids, ladies and gentlemen?'
Add arjun any_3 'Do I hear more?'
Add arjun any_4 'Anyone else looking to bid?'
Add arjun any_5 "Come on, he's worth more than that!"
Add arjun any_6 'Any advance on that?'
Add arjun going_once 'Going once!'
Add arjun going_twice 'Going twice!'
Add arjun last_call 'Last call!'
Add arjun sold 'Sold!'
Add arjun sold_to 'Sold to the'
Add arjun for 'for'
Add arjun unsold 'No bids. Unsold.'
Add arjun no_more_bids "I don't see any more bids. Selling to the"
# --- Match mode commentary
Add arjun match_welcome 'Welcome to the ground! The target is set, and the chase is on.'
Add arjun match_four 'Four! Beautifully timed.'
Add arjun match_six "That's huge! Six!"
Add arjun match_out "Gone! That's out!"
Add arjun match_bowled 'Bowled him! The stumps are shattered!'
Add arjun match_caught 'In the air... and taken! Caught!'
Add arjun match_won 'They have done it! What a chase!'
Add arjun match_lost "And that's the end of the chase. They fall short."
Add arjun rtm_used 'And the right to match is used!'
Add arjun auction_over 'And that concludes the auction. Thank you, everyone!'
foreach ($f in $data.franchises) { Add arjun "team_$($f.id)" $f.name }

# --- Auctioneer: every amount the increment table can reach from any base
# price, up to 35 Cr; beyond that the client stitches crore_N + lakh_N.
$inc = $data.rules.increments
function NextBid([int]$cur) { foreach ($s in $inc) { if ($null -eq $s.below -or $cur -lt $s.below) { return $cur + $s.step } }; return $cur + $inc[-1].step }
$amounts = New-Object 'System.Collections.Generic.SortedSet[int]'
foreach ($b in $data.rules.basePrices) { $a = [int]$b; while ($a -le 3500) { [void]$amounts.Add($a); $a = NextBid $a } }
foreach ($a in $amounts) { Add arjun "amount_$a" (AmountWords $a) }
foreach ($cr in 1..120) { Add arjun "crore_$cr" "$(Words $cr) crore" }
foreach ($l in 5..95) { if ($l % 5 -eq 0) { Add arjun "lakh_$l" "$(Words $l) lakh" } }

# --- Auctioneer: every player's game name
foreach ($p in $data.players) { Add arjun "name_$($p.id)" $p.name }

# --- Auctioneer: pre-auction ceremony
Add arjun ceremony_owners 'Good evening, ladies and gentlemen! Team owners, coaches, analysts, and all our franchise families... a very warm welcome to the Auction Premier League!'
Add arjun ceremony_intro "Before the hammer comes down, let's welcome our ten franchises!"
Add arjun ceremony_finally 'And finally...'
Add arjun ceremony_end 'What a line-up! One more big round of applause for all ten franchises!'

# --- Auctioneer: rules briefing before the first lot (~30 s). Purse and squad
# limits come from the dataset, one line per format.
Add arjun rules_intro "Before we begin, a quick look at today's rules."
foreach ($p in $data.rules.formats.PSObject.Properties) {
  $fmt = $p.Value
  Add arjun "rules_purse_$($p.Name)" "Every franchise has a fixed purse of $(AmountWords $fmt.purse) rupees. Once it's spent, it's gone."
  Add arjun "rules_squad_$($p.Name)" "Each squad needs at least $(Words $fmt.minSquad) players, and can have up to $(Words $fmt.maxSquad), with no more than $(Words $fmt.maxOverseas) overseas players."
}
Add arjun rules_fill 'Finish short of the minimum, and the empty slots are filled with base price players.'
Add arjun rules_retention 'Retained players have already been paid for, from your purse.'
Add arjun rules_rtm 'Each franchise has one Right to Match card, to take back a player from last season, at the final bid.'
Add arjun rules_bidding "When the bidding stops, it's going once... going twice... and sold!"
Add arjun rules_go 'Good luck to all ten franchises. Let the auction begin!'
foreach ($f in $data.franchises) { Add arjun "welcome_team_$($f.id)" "From $($f.city)... please welcome... the $($f.name)!" }

# --- Auctioneer: Right to Match, asked of the card holder after the hammer.
foreach ($f in $data.franchises) { Add arjun "rtm_ask_$($f.id)" "$($f.name), you hold a Right to Match card. Would you like to use it for this player?" }
Add arjun rtm_declined 'No Right to Match.'

# --- Assistant (Kajal)
Add kajal next_player 'Here is the next player.'
Add kajal next_player_2 'Next player, please.'
Add kajal first_player "Here's our first player."
foreach ($f in $data.franchises) {
  Add kajal "rtm_card_$($f.id)" "The $($f.name) hold a Right to Match card."
  Add kajal "rtm_used_$($f.id)" "The $($f.name) use their Right to Match!"
}

# --- Synthesize what's new or changed
$manifestPath = Join-Path $outDir 'voice_lines.json'
$old = @{}
if (Test-Path $manifestPath) {
  ([IO.File]::ReadAllText($manifestPath, $utf8) | ConvertFrom-Json).lines | ForEach-Object { $old["$($_.voice)/$($_.key)"] = $_.text }
}
$voiceId = @{ arjun = 'Arjun'; kajal = 'Kajal' }
$i = 0; $made = 0
foreach ($entry in $lines.Values) {
  $i++
  $dir = Join-Path $outDir $entry.voice
  New-Item -ItemType Directory -Force $dir | Out-Null
  $file = Join-Path $dir "$($entry.key).mp3"
  if ((Test-Path $file) -and $old["$($entry.voice)/$($entry.key)"] -eq $entry.text) { continue }
  aws polly synthesize-speech --profile $Profile --region us-east-1 --engine generative `
    --voice-id $voiceId[$entry.voice] --output-format mp3 --sample-rate 24000 `
    --text $entry.text $file --query RequestCharacters --output text | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Polly failed on $($entry.voice)/$($entry.key)" }
  $made++
  if ($made % 25 -eq 0) { Write-Host "  $i / $($lines.Count) ..." }
}

$manifest = @{
  generatedAt = (Get-Date).ToUniversalTime().ToString('o')
  dataVersion = $data.version
  lines = @($lines.Values | ForEach-Object { [ordered]@{ voice = $_.voice; key = $_.key; text = $_.text; file = "$($_.voice)/$($_.key).mp3" } })
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8)
Write-Host "Done: $($lines.Count) lines ($made synthesized now) -> $outDir"
