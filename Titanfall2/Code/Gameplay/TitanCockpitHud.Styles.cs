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
	.phase-overlay { position: absolute; left: 0; top: 0; width: 100%; height: 100%; opacity: 0; border: 22px solid #76e4ff99; background-color: #162a3a72; backdrop-filter: saturate(20%) contrast(1.1); transition: opacity 0.12s ease; }
	.phase-overlay.active { opacity: 1; }
	.sonar-marker { position: absolute; width: 18px; height: 18px; margin-left: -9px; margin-top: -9px; border: 2px solid #ff7a2e; background-color: #ff601433; transform: rotate(45deg); display: none; }
	.sonar-marker.visible { display: flex; }
	.eyebrow { font-size: 12px; letter-spacing: 1px; color: #b4cedf; }
	.link-header { position: absolute; left: 4.5%; top: 4.5%; flex-direction: column; }
	.chassis { font-size: 19px; letter-spacing: 2px; font-weight: 600; }
	.link-state { font-size: 11px; color: #80bdd6; margin-top: 3px; }
	.upgrade-tier { font-size: 11px; color: #ffde83; margin-top: 3px; letter-spacing: 1px; }
	.compass-bar { position: absolute; left: 50%; top: 3.5%; margin-left: -170px; width: 340px; flex-direction: column; align-items: center; }
	.heading { font-size: 14px; letter-spacing: 3px; color: #d8f2ff; font-weight: 600; }
	.telemetry { font-size: 10px; letter-spacing: 1.5px; color: #88bed6; margin-top: 2px; opacity: 0.85; }
	.embark-progress { position: absolute; left: 50%; top: 54%; margin-left: -140px; width: 280px; opacity: 0; }
	.embark-bar { height: 5px; }
	.reticle { position: absolute; left: 50%; top: 50%; width: 0; height: 0; }
	.reticle-art { position: absolute; left: -26px; top: -12px; width: 24px; height: 24px; }
	.reticle-right { left: 2px; transform: rotate(180deg); }
	.reticle-dot { position: absolute; left: -2px; top: -2px; width: 4px; height: 4px; }
	.reticle.hostile image { background-tint: #ff786d; }
	.hitmarker { position: absolute; left: -12px; top: -12px; width: 24px; height: 24px; opacity: 0; }
	.hitmarker.active { opacity: 0.95; }
	.hm-arm { position: absolute; width: 8px; height: 2px; background-color: #ffffff; }
	.hitmarker.kill .hm-arm { background-color: #ff5548; }
	.hm-tl { left: 1px; top: 4px; transform: rotate(45deg); }
	.hm-tr { right: 1px; top: 4px; transform: rotate(-45deg); }
	.hm-bl { left: 1px; bottom: 4px; transform: rotate(-45deg); }
	.hm-br { right: 1px; bottom: 4px; transform: rotate(45deg); }
	.tone-locks { position: absolute; left: -24px; top: 22px; width: 48px; flex-direction: row; justify-content: center; gap: 6px; display: none; }
	.tone-pip { width: 8px; height: 8px; border: 1px solid #8bdcffaa; transform: rotate(45deg); background-color: #07101888; }
	.tone-pip.filled { background-color: #8bdcff; }
	.tone-pip.locked { border-color: #ff9c59; background-color: #ff7840; }
	.ion-energy-reticle { position: absolute; left: -34px; top: 24px; width: 68px; height: 4px; background-color: #091722aa; border: 1px solid #6cb8de66; display: none; }
	.ion-energy-fill { height: 100%; width: 100%; background-color: #69d1ff; }
	.ion-energy-reticle.ads .ion-energy-fill { background-color: #ffce66; }
	.ion-energy-reticle.low .ion-energy-fill { background-color: #ff6659; }
	.target-state { position: absolute; top: 55.5%; width: 100%; text-align: center; font-size: 13px; color: #ffc09f; }
	.cockpit-message { position: absolute; top: 21%; width: 100%; text-align: center; font-size: 18px; letter-spacing: 1px; }
	.integrity { position: absolute; left: 50%; top: 9.5%; margin-left: -210px; width: 420px; height: 63px; }
	.chassis-art { position: absolute; left: -34px; top: 12px; width: 24px; height: 24px; opacity: 0.72; }
	.integrity > .eyebrow { position: absolute; top: 34px; left: 0; font-size: 10px; opacity: 0.75; }
	.stat { position: absolute; top: 34px; right: 0; font-size: 11px; opacity: 0.85; }
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
	.ammunition { position: absolute; left: 5%; margin-left: 525px; bottom: 7%; width: 250px; height: 80px; align-items: center; }
	.weapon-art { width: 115px; height: 54px; margin-right: 15px; }
	.ammunition .eyebrow { position: absolute; left: 0; top: -9px; font-size: 11px; }
	.rounds { font-size: 34px; font-weight: 600; }
	.rounds.reloading { font-size: 18px; color: #ffd483; }
	.systems { position: absolute; left: 5%; bottom: 7%; width: 510px; height: 166px; }
	.core { position: absolute; width: 156px; height: 156px; }
	.core-gauge { position: absolute; width: 100%; height: 100%; }
	.core-art { position: absolute; left: 48px; top: 42px; width: 60px; height: 60px; }
	.core-name { position: absolute; top: 151px; left: -22px; width: 200px; font-size: 12px; text-align: center; }
	.core-state { position: absolute; top: 112px; width: 100%; text-align: center; font-size: 18px; }
	.core-ready .core-state, .core-active .core-state { color: #ffde83; }
	.abilities { position: absolute; left: 170px; bottom: 4px; flex-direction: row; gap: 10px; }
	.ability { width: 76px; height: 102px; flex-direction: column; align-items: center; opacity: 0.6; }
	.ability.ready { opacity: 1; }
	.ability-art { width: 44px; height: 44px; margin-bottom: 3px; }
	.key { position: absolute; right: -2px; top: 29px; padding: 1px 3px; border: 1px solid #b4cedf99; font-size: 10px; background-color: #07101cbb; }
	.ability-name { font-size: 11px; margin-top: 4px; }
	.ability-state { font-size: 10px; margin-top: 3px; color: #bcd2e2; }
	.ability-bar { position: absolute; left: 6px; bottom: 0; width: 66px; height: 3px; }
	.dash { position: absolute; left: 50%; margin-left: -65px; bottom: 18%; width: 130px; flex-direction: column; align-items: center; }
	.dash-label { font-size: 10px; letter-spacing: 1px; color: #8ab8d0; margin-bottom: 4px; opacity: 0.85; }
	.dash-label.ready { color: #c7f0ff; }
	.dash-bar { width: 110px; height: 6px; }
	.dash-art { position: absolute; width: 110px; height: 6px; object-fit: fill; background-size: 100% 100%; background-tint: #8bdcff; }
	.controls { position: absolute; left: 32%; right: 5%; bottom: 3%; text-align: right; font-size: 11px; color: #a5becf; opacity: 0.72; }
	&.critical .stat, &.critical .cockpit-message { color: #ff786d; }
	&.critical .hull-bar .bar-art { background-tint: #ff786d; }
	&.boarding { background-color: #06142777; }
	&.boarding .embark-progress { opacity: 1; }
	&.boarding .compass-bar, &.boarding .reticle, &.boarding .integrity, &.boarding .systems, &.boarding .ammunition, &.boarding .dash, &.boarding .controls { opacity: 0; }
}
""";
}
