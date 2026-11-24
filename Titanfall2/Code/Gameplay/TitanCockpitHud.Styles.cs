public sealed partial class Titanfall2TitanCockpitHud
{
	// Inline styles travel with the mount; a host game's stylesheet paths differ.
	const string CockpitStyles = """
titanfall2titancockpithud {
	position: absolute;
	width: 100%; height: 100%;
	pointer-events: none;
	font-family: Roboto;
	color: #e4f6ff;
	font-size: 16px;
	text-shadow: 0 1px 3px #000e;
	image { object-fit: contain; background-repeat: no-repeat; background-position: center; background-size: contain; }
	.damage-overlay { position: absolute; width: 100%; height: 100%; opacity: 0; object-fit: fill; background-size: 100% 100%; }
	.eyebrow { font-size: 12px; letter-spacing: 1px; color: #b4cedf; }
	.link-header { position: absolute; left: 5%; top: 6%; flex-direction: column; }
	.chassis { font-size: 18px; letter-spacing: 2px; }
	.link-state { font-size: 11px; color: #80bdd6; margin-top: 3px; }
	.reticle { position: absolute; left: 50%; top: 50%; width: 0; height: 0; }
	.reticle-art { position: absolute; left: -28px; top: -12px; width: 24px; height: 24px; }
	.reticle-right { left: 4px; transform: rotate(180deg); }
	.reticle-dot { position: absolute; left: -2px; top: -2px; width: 4px; height: 4px; }
	.reticle.hostile image { background-tint: #ff786d; }
	.target-state { position: absolute; top: 55%; width: 100%; text-align: center; font-size: 13px; color: #ffc09f; }
	.cockpit-message { position: absolute; top: 29%; width: 100%; text-align: center; font-size: 19px; }
	.integrity { position: absolute; left: 50%; top: 19%; margin-left: -210px; width: 420px; height: 63px; }
	.chassis-art { position: absolute; left: -34px; top: 12px; width: 24px; height: 24px; opacity: 0.65; }
	.integrity > .eyebrow { position: absolute; top: 34px; left: 0; font-size: 10px; opacity: 0.7; }
	.stat { position: absolute; top: 34px; right: 0; font-size: 11px; opacity: 0.8; }
	.shield-stat { position: absolute; top: -16px; right: 0; font-size: 10px; color: #91d7ff; }
	.bar { width: 100%; background-color: #17232c80; }
	.fill { height: 100%; width: 0%; background-color: #91d7ff; }
	.hull-bar { position: absolute; top: 13px; height: 16px; }
	.shield-bar { position: absolute; top: 2px; height: 7px; }
	.hull-bar .fill, .shield-bar .fill, .dash-bar .fill { overflow: hidden; background-color: transparent; }
	.hull-bar .fill { background-color: #dabc7e; }
	.shield-bar .fill, .dash-bar .fill { background-color: #8ac9eb; }
	.hull-segments { position: absolute; width: 100%; height: 100%; }
	.hull-divider { position: absolute; top: 0; width: 2px; height: 100%; background-color: #071018dd; }
	.bar-background, .bar-art { position: absolute; width: 420px; height: 100%; object-fit: fill; background-size: 100% 100%; }
	.hull-bar .bar-art { background-tint: #ffe1a0; }
	.shield-bar .bar-art { background-tint: #8bdcff; }
	.hull-bar .bar-art, .shield-bar .bar-art, .dash-art { mask-size: 100% 100%; mask-repeat: no-repeat; }
	.hull-bar .bar-art, .dash-art { mask-mode: alpha; }
	.shield-bar .bar-art { mask-mode: luminance; }
	.ammunition { position: absolute; left: 5%; margin-left: 370px; bottom: 7%; width: 260px; height: 80px; align-items: center; }
	.weapon-art { width: 115px; height: 54px; margin-right: 15px; }
	.ammunition .eyebrow { position: absolute; left: 0; top: -9px; font-size: 11px; }
	.rounds { font-size: 34px; font-weight: 600; }
	.rounds.reloading { font-size: 18px; color: #ffd483; }
	.systems { position: absolute; left: 5%; bottom: 7%; width: 320px; height: 166px; }
	.core { position: absolute; width: 156px; height: 156px; }
	.core-gauge { position: absolute; width: 100%; height: 100%; }
	.core-art { position: absolute; left: 48px; top: 42px; width: 60px; height: 60px; }
	.core-name { position: absolute; top: 151px; left: -22px; width: 200px; font-size: 12px; text-align: center; }
	.core-state { position: absolute; top: 112px; width: 100%; text-align: center; font-size: 18px; }
	.core-ready .core-state, .core-active .core-state { color: #ffde83; }
	.abilities { position: absolute; left: 175px; bottom: 4px; flex-direction: row; gap: 12px; }
	.ability { width: 78px; height: 102px; flex-direction: column; align-items: center; opacity: 0.6; }
	.ability.ready { opacity: 1; }
	.ability-art { width: 44px; height: 44px; margin-bottom: 3px; }
	.key { position: absolute; right: -2px; top: 29px; padding: 1px 3px; border: 1px solid #b4cedf99; font-size: 10px; background-color: #07101cbb; }
	.ability-name { font-size: 11px; margin-top: 4px; }
	.ability-state { font-size: 10px; margin-top: 3px; color: #bcd2e2; }
	.ability-bar { position: absolute; left: 6px; bottom: 0; width: 66px; height: 3px; }
	.dash { position: absolute; left: 50%; margin-left: -50px; bottom: 24%; width: 100px; height: 6px; }
	.dash-bar { height: 6px; }
	.dash-art { position: absolute; width: 100px; height: 6px; object-fit: fill; background-size: 100% 100%; background-tint: #8bdcff; }
	.controls { position: absolute; left: 32%; right: 7%; bottom: 3%; text-align: right; font-size: 11px; color: #a5becf; opacity: 0.7; }
	&.critical .stat, &.critical .cockpit-message { color: #ff786d; }
	&.critical .hull-bar .bar-art { background-tint: #ff786d; }
	&.boarding { background-color: #06142777; }
	&.boarding .reticle, &.boarding .integrity, &.boarding .systems, &.boarding .ammunition, &.boarding .dash, &.boarding .controls { opacity: 0; }
}
""";
}
