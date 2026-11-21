#include <cstdint>

namespace Math
{
	uint64_t Floor(uint64_t value) {
		return value; // because value is an unsigned int, which is already floored
	}

	double Floor(double value) {
		// TODO
		return value;
	}

	uint64_t Ceil(uint64_t numerator, uint64_t denominator) {
		if (numerator % denominator != 0)
			return (numerator / denominator) + 1;
		else
			return numerator / denominator;
	}

	double Ceil(double value) {
		// TODO
		return value;
	}
}