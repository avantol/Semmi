/*
 *
 *  GridSquares.cs
 *
 *  Provides conversions between Maidenhead Locators ("grid squares")
 *  and latitude/longitude pairs.  Also provides distance and bearing
 *  calculations between two points, either as locators or lat/lon pairs.
 *
 *  Adapted from BD_2004.pas, originally by:
 *     + Michael R. Owen, W9IP,
 *     + Paul Wade, N1BWT, W1GHZ
 *  Original Pascal comments are captured in braces { }.
 *  Original references captured inline.
 *
 *  C# translation and adaptation by Matt Roberts, KK5JY.
 *     Updated 2023-02-20: Fix coord-to-grid conversion.
 *
 */

using System;

namespace KK5JY.Geo {
	/// <summary>
	/// Calculations for Maidenhead grid squares and geodetic coordinates.
	/// </summary>
	public static class GridSquares {
		#region Pascal Compatability Functions
		private static int ord(char ch) {
			return (int)(ch);
		}

		private static char ToChar(double i) {
			return (char)(Convert.ToInt16(Math.Round(i)));
		}
		#endregion

		#region Utility Methods
		/// <summary>
		/// Validate grid square string
		/// </summary>
		/// <param name="grid">The grid square string</param>
		/// <returns>True if valid</returns>
		public static bool CheckGrid(string grid) {
			if (String.IsNullOrEmpty(grid))
				return false;

			bool error = false;
			int i = 0;
			if (grid.Length == 4)
				grid = grid + "LL"; // choose middle if only 4-character
			char[] g = grid.ToCharArray();
			do {
				++i;
				if ((i == 2) || (i == 3)) {
					if ((ord(g[i]) >= '0') && (ord(g[i]) <= '9'))
						error = false;  // 2nd two characters are numbers
					else
						error = true;
				} else if ((ord(g[i]) >= 'A') && (ord(g[i]) <= 'Z')) {
					error = false;      // first and last 2 characters are letters
				} else if ((ord(g[i]) >= 'a') && (ord(g[i]) <= 'z')) {
					g[i] = (char)(g[i] - 'a' + 'A');
					error = false;
				} else error = true;
			} while (i != 5 || error);

			return !error;
		}

		/// <summary>
		/// finds the lat/lon of center of the sub-square
		/// </summary>
		/// <param name="grid">Grid-squre string</param>
		/// <param name="lat">Latitude of the center of the square</param>
		/// <param name="lon">Longitude of the center of the square</param>
		public static void GridCenter(string grid, out double lat, out double lon) {
			if (String.IsNullOrEmpty(grid))
				throw new ArgumentException("Grid argument was empty");

			if (grid.Length == 4)
				grid = grid + "LL"; // {choose middle if only 4-character}
			if (grid.Length != 6)
				throw new ArgumentException("Invalid grid square length");

			if (!CheckGrid(grid))
				throw new ArgumentException("Grid argument was invalid");

			double lonmin, londeg, latmin, latdeg;
			grid = grid.ToUpper();

			lonmin = (5.0 * (ord(grid[4]) - ord('A'))) + 2.5;    // center
			londeg = 180.0 - (20.0 * (ord(grid[0]) - ord('A')))  // tens of deg
						- (2.0 * (ord(grid[2]) - ord('0')));     // two deg
			lon = Math.Abs(londeg - (lonmin / 60.0));
			if (grid[0] <= 'I')
				lon = -lon;

			latdeg = -90.0 + (10.0 * (ord(grid[1]) - ord('A')))  // tens of deg
					+ (ord(grid[3]) - ord('0'));                 // degrees
			latmin = 2.5 * (ord(grid[5]) - ord('A'))             // minutes
					   + 1.25;                                   // for center
			lat = Math.Abs(latdeg + (latmin / 60.0));
			if (grid[1] <= 'I')
				lat = -lat;
		}

		/// <summary>
		/// Converts latitude and longitude to grid square.
		/// </summary>
		/// <param name="LocLat">Latitude</param>
		/// <param name="LocLon">Longitude</param>
		/// <returns>Grid square string</returns>
		public static string CoordinatesToGrid(double LocLat, double LocLon) {
			double C;
			double G4, R, M, L4;
			char M1, M2, M3, M4, M5, M6; // these are the output symbols
			string GS; // the output as a string

			G4 = /*-*/LocLon + 180;
			C = Math.Truncate(G4 / 20);
			M1 = ToChar(C + 65);

			R = Math.Abs(LocLon / 20);
			/*
			{error here:
				R = INT(((R-INT(R))*20+0.001));
				the +0.001 at the end causes rollover in the last 3 seconds of the grid}
			{should be:}
			*/
			R = Math.Truncate((R - Math.Truncate(R)) * 20);
			C = Math.Truncate(R / 2);
			if (LocLon < 0)
				C = Math.Abs(C - 9);
			M3 = ToChar(C + 48);

			M = Math.Abs(LocLon * 60);
			M = ((M / 120) - Math.Truncate(M / 120)) * 120;
			M = Math.Truncate(M + 0.001);
			C = Math.Truncate(M / 5);
			if (LocLon < 0)
				C = Math.Abs(C - 23);
			M5 = ToChar(C + 97);

			L4 = LocLat + 90;
			C = Math.Truncate(L4 / 10);
			M2 = ToChar(C + 65);

			R = Math.Abs(LocLat / 10);
			/*
			{error here:
				R = INT(((R-INT(R))*10+0.001));
				the +0.001 at the end causes rollover in the last 3 seconds of the grid}
			{should be:}
			*/
			R = Math.Truncate(((R - Math.Truncate(R)) * 10));
			C = Math.Truncate(R);
			if (LocLat < 0)
				C = Math.Abs(C - 9);
			M4 = ToChar(C + 48);

			M = Math.Abs(LocLat * 60);
			M = ((M / 60) - Math.Truncate(M / 60)) * 60;
			C = Math.Truncate(M / 2.5);
			if (LocLat < 0)
				C = Math.Abs(C - 23);
			M6 = ToChar(C + 97);

			// put it all together
			GS = String.Format("{0}{1}{2}{3}{4}{5}", M1, M2, M3, M4, M5, M6);

			return GS;
		}

		/// <summary>
		/// Calculate distance and bearing between two grid squares on the globe.
		/// </summary>
		/// <param name="GridFrom">Starting latitude</param>
		/// <param name="GridTo">Ending latitude</param>
		/// <param name="AzimuthFrom">Azimuth to ending point</param>
		/// <param name="AzimuthTo">Long-path azimuth to ending point</param>
		/// <param name="Distance">Distance to ending point</param>
		public static void CalculateDistanceAndBearing(
		   string GridFrom,
		   string GridTo,
		   out double AzimuthFrom,
		   out double AzimuthTo,
		   out double Distance) {
			// validate the inputs
			if (!CheckGrid(GridFrom))
				throw new ArgumentException("GridFrom is invalid");
			if (!CheckGrid(GridTo))
				throw new ArgumentException("GridTo is invalid");

			// handle zero-length calculation, which the math doesn't like
			if (GridFrom.Trim().Equals(GridTo.Trim(), StringComparison.InvariantCultureIgnoreCase)) {
				AzimuthFrom = AzimuthTo = Distance = 0.0;
				return;
			}

			// calculate the grid square center coordinates
			double LatFrom, LonFrom, LatTo, LonTo;
			GridCenter(GridFrom, out LatFrom, out LonFrom);
			GridCenter(GridTo, out LatTo, out LonTo);

			// calculate the azimuth and distance
			CalculateDistanceAndBearing(
				LatFrom, LonFrom,
				LatTo, LonTo,
				out AzimuthFrom, out AzimuthTo, out Distance);
		}

		/// <summary>
		/// Calculate distance and bearing between two points on the globe.
		/// </summary>
		/// <param name="LatitudeFrom">Starting latitude</param>
		/// <param name="LongitudeFrom">Starting longitude</param>
		/// <param name="LatitudeTo">Ending latitude</param>
		/// <param name="LongitudeTo">Ending longitude</param>
		/// <param name="AzimuthFrom">Azimuth to ending point</param>
		/// <param name="AzimuthTo">Long-path azimuth to ending point</param>
		/// <param name="Distance">Distance to ending point</param>
		public static void CalculateDistanceAndBearing(
		   double LatitudeFrom, double LongitudeFrom,
		   double LatitudeTo, double LongitudeTo,
		   out double AzimuthFrom,
		   out double AzimuthTo,
		   out double Distance) {
			// handle zero-length calculation, which the math doesn't like
			if (LatitudeFrom == LatitudeTo && LongitudeFrom == LongitudeTo) {
				AzimuthFrom = AzimuthTo = Distance = 0.0;
				return;
			}

			/*
			{ [Adapted from code] Taken directly from:                     }
			{ Thomas, P.D., 1970, Spheroidal Geodesics, reference systems, }
			{     & local geometry, U.S. Naval Oceanographic Office SP-138,}
			{     165 pp.                                                  }
			{ assumes North Latitude and East Longitude are positive       }
			{ EpLat, EpLon = MyLat, MyLon                                  }
			{ Stlat, Stlon = HisLat, HisLon                                }
			{ Az, BAz = direct & reverse azimuith                          }
			{ Dist = Dist (km); Deg = central angle, discarded             }
			*/
			const double AL = 6378206.4; //  {Clarke 1866 ellipsoid}
			const double BL = 6356583.8;
			const double D2R = Math.PI / 180.0; //  {degrees to radians conversion factor}
			const double Pi2 = 2.0 * Math.PI;
			double BOA, F,
				   P1R, P2R,
				   L1R, L2R,
				   DLR,
				   T1R, T2R,
				   TM, DTM,
				   STM, CTM,
				   SDTM, CDTM,
				   KL, KK,
				   SDLMR, L,
				   CD, DL, SD,
				   T, U, V, D, X, E, Y, A,
				   FF64, TDLPM,
				   HAPBR, HAMBR,
				   A1M2, A2M1;

			AzimuthFrom = AzimuthTo = Distance = 0.0;
			BOA = BL / AL;
			F = 1.0 - BOA;
			P1R = LatitudeFrom * D2R;
			P2R = LatitudeTo * D2R;
			L1R = LongitudeFrom * D2R;
			L2R = LongitudeTo * D2R;
			DLR = L2R - L1R;
			T1R = Math.Atan(BOA * Math.Tan(P1R));
			T2R = Math.Atan(BOA * Math.Tan(P2R));
			TM = (T1R + T2R) / 2.0;
			DTM = (T2R - T1R) / 2.0;
			STM = Math.Sin(TM);
			CTM = Math.Cos(TM);
			SDTM = Math.Sin(DTM);
			CDTM = Math.Cos(DTM);
			KL = STM * CDTM;
			KK = SDTM * CTM;
			SDLMR = Math.Sin(DLR / 2.0);
			L = SDTM * SDTM + SDLMR * SDLMR * (CDTM * CDTM - STM * STM);
			CD = 1.0 - 2.0 * L;
			DL = Math.Acos(CD); // was ArcCos(...)
			SD = Math.Sin(DL);
			T = DL / SD;
			U = 2.0 * KL * KL / (1.0 - L);
			V = 2.0 * KK * KK / L;
			D = 4.0 * T * T;
			X = U + V;
			E = -2.0 * CD;
			Y = U - V;
			A = -D * E;
			FF64 = F * F / 64.0;
			Distance = AL * SD * (T - (F / 4.0) * (T * X - Y) + FF64 * (X * (A + (T - (A + E) / 2.0) * X) + Y * (-2.0 * D + E * Y) + D * X * Y)) / 1000.0;
			//Deg = Dist / 111.195;
			TDLPM = Math.Tan((DLR + (-((E * (4.0 - X) + 2.0 * Y) * ((F / 2.0) * T + FF64 * (32.0 * T + (A - 20.0 * T) * X - 2.0 * (D + 2.0) * Y)) / 4.0) * Math.Tan(DLR))) / 2.0);
			HAPBR = Math.Atan2(SDTM, (CTM * TDLPM));
			HAMBR = Math.Atan2(CDTM, (STM * TDLPM));
			A1M2 = Pi2 + HAMBR - HAPBR;
			A2M1 = Pi2 - HAMBR - HAPBR;
		b1: if ((A1M2 >= 0.0) && (A1M2 < Pi2)) goto b5;
			else goto b2;
		b2: if (A1M2 >= Pi2) goto b3;
			else goto b4;
		b3: A1M2 = A1M2 - Pi2;
			goto b1;
		b4: A1M2 = A1M2 + Pi2;
			goto b1;
		b5: if ((A2M1 >= 0.0) && (A2M1 < Pi2)) goto b9;
			else goto b6;
		b6: if (A2M1 >= Pi2) goto b7;
			else goto b8;
		b7: A2M1 = A2M1 - Pi2;
			goto b5;
		b8: A2M1 = A2M1 + Pi2;
			goto b5;
		b9: AzimuthFrom = A1M2 / D2R;
			AzimuthTo = A2M1 / D2R;
		}
		#endregion
	}
}