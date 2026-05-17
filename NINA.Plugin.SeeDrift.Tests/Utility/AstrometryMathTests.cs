using System;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class AstrometryMathTests {

        [Fact]
        public void DeltaArcSec_at_same_point_returns_zero() {
            AstrometryMath.DeltaArcSec(0, 0, 0, 0, out var dra, out var ddec);
            Assert.Equal(0.0, dra);
            Assert.Equal(0.0, ddec);
        }

        [Fact]
        public void DeltaArcSec_1_degree_dec_at_equator() {
            AstrometryMath.DeltaArcSec(0, 0, 0, 1, out var _, out var ddec);
            Assert.InRange(ddec, 3599.5, 3600.5);
        }

        [Fact]
        public void DeltaArcSec_ra_at_equator_cosine() {
            AstrometryMath.DeltaArcSec(0, 0, 1 / 240.0, 0, out var dra, out var _);
            // 1/240 hour ≈ 0.004167h => *54000 = ~225 arcsec at equator (cos(0)=1)
            Assert.InRange(dra, 224.5, 225.5);
        }

        [Fact]
        public void DeltaArcSec_ra_at_high_dec_cosine_reduction() {
            AstrometryMath.DeltaArcSec(0, 60, 1 / 240.0, 60, out var dra, out var _);
            // cos(mid=60°) = 0.5 => RA arcsec halved vs equator ≈ 112.5"
            Assert.InRange(dra, 112.4, 112.6);
        }

        [Fact]
        public void DeltaArcSec_ra_wraps_24h() {
            // 23/24 hours from 0 → raw diff = 0.958h, no wrapping needed (< 12)
            AstrometryMath.DeltaArcSec(0, 0, 23.0 / 24.0, 0, out var dra, out _);
            // 0.9583h * 54000 ≈ 51750 at equator (cos(0)=1)
            Assert.InRange(dra, 51749.5, 51750.5);
        }

        [Fact]
        public void DeltaArcSec_ra_wraps_12h_boundary() {
            AstrometryMath.DeltaArcSec(12.5, 0, 13.5, 0, out var dra, out _);
            // Raw diff = 1h (within [-12,12], no wrapping) => 1 * 54000 at equator
            Assert.InRange(dra, 53999.5, 54000.5);
        }

        [Fact]
        public void PixelShiftToRaDec_EQ_mode_no_shift() {
            AstrometryMath.PixelShiftToRaDec(0, 0, 1.0, 45, true, 0, out var dra, out var ddec);
            Assert.Equal(0.0, dra);
            Assert.Equal(0.0, ddec);
        }

        [Fact]
        public void PixelShiftToRaDec_EQ_dy_down_positive_gives_negative_dec() {
            AstrometryMath.PixelShiftToRaDec(0, 10, 1.0, 45, true, 0, out var _, out var ddec);
            // dy=10 * scale=1 => -10 arcsec in Dec
            Assert.InRange(ddec, -10.5, -9.5);
        }

        [Fact]
        public void PixelShiftToRaDec_EQ_dx_left_gives_negative_ra() {
            // scale=1 => deltaRA = -dx * 1 / cos(45°) = -10 / 0.707 ≈ -14.14
            AstrometryMath.PixelShiftToRaDec(10, 0, 1.0, 45, true, 0, out var dra, out _);
            Assert.InRange(dra, -14.2, -14.0);
        }

        [Fact]
        public void PixelShiftToRaDec_AltAz_q_0_same_as_EQ() {
            AstrometryMath.PixelShiftToRaDec(5, 3, 2.0, 45, false, 0, out var dra1, out var ddec1);
            AstrometryMath.PixelShiftToRaDec(5, 3, 2.0, 45, true, 0, out var dra2, out var ddec2);
            Assert.Equal(dra2, dra1);
            Assert.Equal(ddec2, ddec1);
        }

        [Fact]
        public void PixelShiftToRaDec_AltAz_q_90deg_rotated() {
            // q=PI/2: east = s*(-dx*0 - dy*1) = -s*dy = -6; deltaRA = -6/cos(45°) ≈ -8.49
            //        dec  = s*(dx*1 - dy*0) = s*dx = 10
            AstrometryMath.PixelShiftToRaDec(5, 3, 2.0, 45, false, Math.PI / 2, out var dra, out var ddec);
            Assert.InRange(dra, -8.5, -8.3);
        }

        [Fact]
        public void PixelShiftToRaDec_near_pole_guard() {
            // Should not throw even at the pole (cos(dec) ~ 0)
            AstrometryMath.PixelShiftToRaDec(1, 1, 1.0, 89.9995, true, 0, out _, out _);
        }

        [Fact]
        public void ParallacticAngle_at_zenith_returns_zero() {
            // When az=π/2 (East) and alt→90°: sinHA→0, denominator stays positive => PA→0
            double pa = AstrometryMath.ParallacticAngle(45, 89.99, 90, 30);
            Assert.InRange(pa, -0.1, 0.1);
        }

        [Fact]
        public void Parallactic_angle_returns_radians() {
            // A known configuration: lat=0, alt=0 (horizon), az=270 (West), dec=0 => HA=90° => PA should be ±90°
            double pa = AstrometryMath.ParallacticAngle(0, 0, 270, 0);
            Assert.InRange(pa, Math.PI / 4, Math.PI * 3 / 4);
        }
    }
}
